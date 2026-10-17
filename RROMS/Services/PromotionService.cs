using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;

namespace RROMS.Services;

/// One cart line's resolved discount, in cents-accurate decimal - never a
/// percentage the browser could recompute differently.
public record PromotionLineDiscount(int MenuItemId, int? PromotionId, decimal DiscountAmount);

/// The outcome of checking a promo code typed in the cart.
public record PromoCodeCheck(bool Ok, string Message, Promotion? Promotion, decimal Discount);

public class PromotionCalculationResult
{
    public List<PromotionLineDiscount> Lines { get; set; } = new();
    public decimal TotalDiscount => Lines.Sum(l => l.DiscountAmount);
}

/// 
/// The single place that decides which promotion (if any) applies to a dish,
/// for a given customer, on a given day - used identically by the menu
/// browsing pages (for display) and by order placement (for the amount the
/// customer is actually charged), so the two can never drift apart. Every
/// eligibility rule from the brief is enforced here, on the server, against
/// real database state: scope (storewide / category / specific item),
/// specific eligible customer emails, allowed days of week, a date range,
/// a minimum order amount, and total/per-customer redemption caps.
/// 
public interface IPromotionService
{
    Task<Dictionary<int, decimal>> GetDisplayDiscountsAsync(IEnumerable<MenuItem> menuItems, string? customerEmail);

    /// <param name="cartSubtotal">The whole cart before discounts (set menus and wine by the glass included), used for "minimum order" rules.
    /// When null it is worked out from the lines passed in.</param>
    Task<PromotionCalculationResult> CalculateOrderDiscountsAsync(
        string customerId, string? customerEmail, IEnumerable<(MenuItem Item, int Quantity)> lines, DateTime when, decimal? cartSubtotal = null);

    /// Checks a promo code against the cart and says how much it would take off. Nothing is recorded until the order is placed.
    Task<PromoCodeCheck> CheckCodeAsync(string code, string customerId, string? customerEmail,
        decimal grossSubtotal, IEnumerable<(MenuItem Item, decimal Gross)> dishLines, DateTime when);

    /// Notes that a code promotion was used by an order, so its use limits count it.
    Task RecordCodeRedemptionAsync(int promotionId, int orderId, string customerId, decimal discount);

    Task RecordRedemptionsAsync(PromotionCalculationResult result, int orderId, string customerId);
}

public class PromotionService : IPromotionService
{
    private readonly ApplicationDbContext _context;

    public PromotionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<int, decimal>> GetDisplayDiscountsAsync(IEnumerable<MenuItem> menuItems, string? customerEmail)
    {
        var items = menuItems.ToList();
        var result = new Dictionary<int, decimal>();
        if (items.Count == 0) return result;

        var now = DateTime.UtcNow;
        var candidates = (await GetCandidatePromotionsAsync(now))
            .Where(p => p.IsDayAllowed(now.DayOfWeek))
            .Where(p => IsEligibleForCustomer(p, customerEmail))
            // A minimum-order-amount promotion depends on the whole cart, which isn't
            // known yet while just browsing - don't advertise a price the customer
            // might not end up qualifying for at checkout.
            .Where(p => p.MinimumOrderAmount == null)
            .ToList();

        if (candidates.Count == 0) return result;

        foreach (var item in items)
        {
            var best = BestMatch(candidates, item);
            if (best != null) result[item.Id] = best.DiscountPercent;
        }

        return result;
    }

    public async Task<PromotionCalculationResult> CalculateOrderDiscountsAsync(
        string customerId, string? customerEmail, IEnumerable<(MenuItem Item, int Quantity)> lines, DateTime when, decimal? cartSubtotal = null)
    {
        var lineList = lines.ToList();
        var result = new PromotionCalculationResult();
        if (lineList.Count == 0) return result;

        var wholeCart = cartSubtotal ?? lineList.Sum(l => l.Item.Price * l.Quantity);

        var candidates = (await GetCandidatePromotionsAsync(when))
            .Where(p => p.IsDayAllowed(when.DayOfWeek))
            .Where(p => IsEligibleForCustomer(p, customerEmail))
            .Where(p => p.MinimumOrderAmount == null || p.MinimumOrderAmount <= wholeCart)
            .ToList();

        if (candidates.Count == 0) return result;

        // Only pull usage counts for promotions that could plausibly match at least
        // one line here - no point querying redemptions for anything irrelevant.
        var relevantIds = candidates
            .Where(p => lineList.Any(l => MatchesScope(p, l.Item)))
            .Select(p => p.Id)
            .Distinct()
            .ToList();

        if (relevantIds.Count == 0) return result;

        var totalCounts = await _context.PromotionRedemptions
            .Where(r => relevantIds.Contains(r.PromotionId))
            .GroupBy(r => r.PromotionId)
            .Select(g => new { PromotionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PromotionId, g => g.Count);

        var customerCounts = await _context.PromotionRedemptions
            .Where(r => relevantIds.Contains(r.PromotionId) && r.CustomerId == customerId)
            .GroupBy(r => r.PromotionId)
            .Select(g => new { PromotionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PromotionId, g => g.Count);

        bool HasRemainingCapacity(Promotion p) =>
            (!p.MaxRedemptionsTotal.HasValue || totalCounts.GetValueOrDefault(p.Id) < p.MaxRedemptionsTotal.Value) &&
            (!p.MaxRedemptionsPerCustomer.HasValue || customerCounts.GetValueOrDefault(p.Id) < p.MaxRedemptionsPerCustomer.Value);

        var usable = candidates.Where(HasRemainingCapacity).ToList();
        if (usable.Count == 0) return result;

        foreach (var (item, quantity) in lineList)
        {
            var best = BestMatch(usable, item);
            if (best == null) continue;

            var lineTotal = item.Price * quantity;
            var discount = Math.Round(lineTotal * best.DiscountPercent / 100m, 2);
            if (discount <= 0) continue;

            result.Lines.Add(new PromotionLineDiscount(item.Id, best.Id, discount));
        }

        return result;
    }

    public async Task RecordRedemptionsAsync(PromotionCalculationResult result, int orderId, string customerId)
    {
        if (result.Lines.Count == 0) return;

        var redemptions = result.Lines
            .Where(l => l.PromotionId.HasValue)
            .GroupBy(l => l.PromotionId!.Value)
            .Select(g => new PromotionRedemption
            {
                PromotionId = g.Key,
                OrderId = orderId,
                CustomerId = customerId,
                DiscountAmount = g.Sum(l => l.DiscountAmount),
                RedeemedAt = DateTime.UtcNow
            });

        _context.PromotionRedemptions.AddRange(redemptions);
        await _context.SaveChangesAsync();
    }

    /// Promotions that apply without being asked for: running today, a percentage, and no code.
    private async Task<List<Promotion>> GetCandidatePromotionsAsync(DateTime when)
    {
        var today = when.Date;
        return await _context.Promotions
            .Where(p => p.IsActive && p.StartDate <= today && p.EndDate >= today && p.Code == null && p.DiscountPercent > 0)
            .ToListAsync();
    }

    public async Task<PromoCodeCheck> CheckCodeAsync(string code, string customerId, string? customerEmail,
        decimal grossSubtotal, IEnumerable<(MenuItem Item, decimal Gross)> dishLines, DateTime when)
    {
        var wanted = (code ?? string.Empty).Trim();
        if (wanted.Length == 0)
            return new PromoCodeCheck(false, "Type a code first.", null, 0m);

        var lower = wanted.ToLower();
        var promo = await _context.Promotions.FirstOrDefaultAsync(p => p.Code != null && p.Code.ToLower() == lower);
        if (promo == null)
            return new PromoCodeCheck(false, "We do not recognise that code. Please check it and try again.", null, 0m);

        var today = when.Date;
        if (!promo.IsActive || promo.EndDate.Date < today)
            return new PromoCodeCheck(false, $"{promo.Code} has ended.", promo, 0m);
        if (promo.StartDate.Date > today)
            return new PromoCodeCheck(false, $"{promo.Code} starts on {promo.StartDate:d MMMM}.", promo, 0m);
        if (!promo.IsDayAllowed(when.DayOfWeek))
            return new PromoCodeCheck(false, $"{promo.Code} is not valid today.", promo, 0m);
        if (!IsEligibleForCustomer(promo, customerEmail))
            return new PromoCodeCheck(false, $"{promo.Code} is not available on your account.", promo, 0m);
        if (promo.MinimumOrderAmount.HasValue && grossSubtotal < promo.MinimumOrderAmount.Value)
            return new PromoCodeCheck(false, $"{promo.Code} needs an order of {Ui.Rm(promo.MinimumOrderAmount.Value)} or more.", promo, 0m);

        if (promo.MaxRedemptionsTotal.HasValue &&
            await _context.PromotionRedemptions.CountAsync(r => r.PromotionId == promo.Id) >= promo.MaxRedemptionsTotal.Value)
            return new PromoCodeCheck(false, $"{promo.Code} has been used as many times as it can be.", promo, 0m);
        if (promo.MaxRedemptionsPerCustomer.HasValue &&
            await _context.PromotionRedemptions.CountAsync(r => r.PromotionId == promo.Id && r.CustomerId == customerId) >= promo.MaxRedemptionsPerCustomer.Value)
            return new PromoCodeCheck(false, $"You have already used {promo.Code} as often as it allows.", promo, 0m);

        // What the code can take money off: the whole bill, or only the dishes in its category or its one dish.
        var scoped = promo.MenuItemId == null && promo.MenuCategoryId == null
            ? grossSubtotal
            : dishLines.Where(l => MatchesScope(promo, l.Item)).Sum(l => l.Gross);
        if (scoped <= 0)
            return new PromoCodeCheck(false, $"{promo.Code} does not apply to anything in your cart.", promo, 0m);

        var discount = promo.FixedAmountOff is > 0
            ? Math.Min(promo.FixedAmountOff.Value, scoped)
            : Math.Round(scoped * promo.DiscountPercent / 100m, 2);
        if (discount <= 0)
            return new PromoCodeCheck(false, $"{promo.Code} does not take anything off this order.", promo, 0m);

        return new PromoCodeCheck(true, Describe(promo), promo, discount);
    }

    /// "AUTUMN20 applied: RM 20 off orders over RM 300"
    public static string Describe(Promotion promo)
    {
        var off = promo.FixedAmountOff is > 0 ? $"{Ui.Rm(promo.FixedAmountOff.Value)} off" : $"{promo.DiscountPercent:0.##}% off";
        var min = promo.MinimumOrderAmount is > 0 ? $" orders over {Ui.Rm(promo.MinimumOrderAmount.Value)}" : "";
        return $"{promo.Code} applied: {off}{min}";
    }

    public async Task RecordCodeRedemptionAsync(int promotionId, int orderId, string customerId, decimal discount)
    {
        _context.PromotionRedemptions.Add(new PromotionRedemption
        {
            PromotionId = promotionId,
            OrderId = orderId,
            CustomerId = customerId,
            DiscountAmount = discount,
            RedeemedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private static bool IsEligibleForCustomer(Promotion promo, string? customerEmail)
    {
        if (!promo.IsEligibilityRestricted) return true;
        if (string.IsNullOrWhiteSpace(customerEmail)) return false;
        return promo.GetEligibleEmails().Contains(customerEmail.Trim().ToLowerInvariant());
    }

    /// True if the promotion's scope (storewide / category / specific item)
    /// covers the given menu item.
    private static bool MatchesScope(Promotion p, MenuItem item) =>
        p.MenuItemId == item.Id ||
        (p.MenuItemId == null && p.MenuCategoryId != null && p.MenuCategoryId == item.MenuCategoryId) ||
        (p.MenuItemId == null && p.MenuCategoryId == null);

    /// Among already-filtered candidates, the single best (highest percent)
    /// promotion that covers this item - discounts never stack.
    private static Promotion? BestMatch(List<Promotion> candidates, MenuItem item) => candidates
        .Where(p => MatchesScope(p, item))
        .OrderByDescending(p => p.DiscountPercent)
        .ThenBy(p => p.Id)
        .FirstOrDefault();
}
