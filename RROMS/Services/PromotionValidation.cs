using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;

namespace RROMS.Services;

/// What the promotion form posts. Nothing here is trusted until <see cref="PromotionValidation"/> has checked it.
public class PromotionForm
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// "percent" or "amount".
    public string Kind { get; set; } = "percent";
    public decimal? DiscountPercent { get; set; }
    public decimal? FixedAmountOff { get; set; }
    public string? Code { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// "storewide", "category" or "item".
    public string Scope { get; set; } = "storewide";
    public int? MenuItemId { get; set; }
    public int? MenuCategoryId { get; set; }
    /// "all", "tier" (members of the chosen ranks) or "specific" (named guests, by email).
    public string Eligibility { get; set; } = "all";
    public string? EligibleEmails { get; set; }
    /// The membership ranks (RewardTier ids) ticked when Eligibility is "tier".
    public List<int> EligibleTierIds { get; set; } = new();
    public List<int> AllowedDays { get; set; } = new();
    public decimal? MinimumOrderAmount { get; set; }
    public int? MaxRedemptionsTotal { get; set; }
    public int? MaxRedemptionsPerCustomer { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NotifyCustomers { get; set; }
}

/// The cleaned-up values to store when <see cref="Valid"/> is true.
public record PromotionValidationResult(
    bool Valid, string Error, string Title, string? Description, decimal DiscountPercent, decimal? FixedAmountOff, string? Code,
    DateTime StartDate, DateTime EndDate, int? MenuItemId, int? MenuCategoryId, string? EligibleEmails, string? EligibleTierIds, int? AllowedDaysMask)
{
    public static PromotionValidationResult Fail(string error) =>
        new(false, error, string.Empty, null, 0, null, null, default, default, null, null, null, null, null);
}

/// 
/// Server-side validation for creating or editing a Promotion (Admin > Promotions). Every value comes from a posted
/// form, so nothing is trusted until it has been checked against real database records (a dish or category that exists,
/// sane numbers, a code nobody else has) - the same "never trust the browser" rule that applies when a discount is
/// actually taken off at checkout.
/// 
public static class PromotionValidation
{
    private static readonly Regex CodeShape = new("^[A-Za-z0-9_-]{3,30}$", RegexOptions.Compiled);
    private static readonly Regex EmailShape = new(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$", RegexOptions.Compiled);

    public static async Task<PromotionValidationResult> ValidateAsync(ApplicationDbContext context, PromotionForm f)
    {
        var title = (f.Title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > 120) return PromotionValidationResult.Fail("Give the promotion a title (up to 120 characters).");
        var description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim();
        if (description != null && description.Length > 400) return PromotionValidationResult.Fail("Keep the description under 400 characters.");

        if (f.StartDate == null || f.EndDate == null) return PromotionValidationResult.Fail("Choose when the promotion starts and ends.");
        if (f.EndDate.Value.Date < f.StartDate.Value.Date) return PromotionValidationResult.Fail("The end date cannot be before the start date.");

        // A code makes it a code promotion: it never applies by itself, only when a guest types it in the cart.
        string? code = null;
        if (!string.IsNullOrWhiteSpace(f.Code))
        {
            code = f.Code.Trim();
            if (!CodeShape.IsMatch(code)) return PromotionValidationResult.Fail("A promo code is 3 to 30 letters, numbers, hyphens or underscores, with no spaces.");
            code = code.ToUpperInvariant();
            var lower = code.ToLower();
            if (await context.Promotions.AnyAsync(p => p.Id != f.Id && p.Code != null && p.Code.ToLower() == lower))
                return PromotionValidationResult.Fail($"The code {code} is already used by another promotion.");
        }

        decimal percent = 0m;
        decimal? fixedAmount = null;
        if (string.Equals(f.Kind, "amount", StringComparison.OrdinalIgnoreCase))
        {
            if (code == null) return PromotionValidationResult.Fail("An amount off needs a promo code. Guests type the code in the cart to use it.");
            if (f.FixedAmountOff is not > 0 or > 100000) return PromotionValidationResult.Fail("Enter how much it takes off, from RM 0.01 to RM 100,000.");
            fixedAmount = Math.Round(f.FixedAmountOff.Value, 2);
        }
        else
        {
            if (f.DiscountPercent is not >= 1 or > 100) return PromotionValidationResult.Fail("The discount must be between 1% and 100%.");
            percent = Math.Round(f.DiscountPercent.Value, 2);
        }

        if (f.MinimumOrderAmount is < 0 or > 100000) return PromotionValidationResult.Fail("The minimum order must be between RM 0 and RM 100,000.");
        if (f.MaxRedemptionsTotal is < 1 || f.MaxRedemptionsPerCustomer is < 1) return PromotionValidationResult.Fail("Use limits must be at least 1.");

        int? itemId = null, categoryId = null;
        switch ((f.Scope ?? "storewide").ToLowerInvariant())
        {
            case "item":
                if (f.MenuItemId == null || !await context.MenuItems.AnyAsync(m => m.Id == f.MenuItemId))
                    return PromotionValidationResult.Fail("Choose a dish from the list.");
                itemId = f.MenuItemId;
                break;
            case "category":
                if (f.MenuCategoryId == null || !await context.MenuCategories.AnyAsync(c => c.Id == f.MenuCategoryId))
                    return PromotionValidationResult.Fail("Choose a category from the list.");
                categoryId = f.MenuCategoryId;
                break;
        }

        string? emails = null, tierIds = null;
        if (string.Equals(f.Eligibility, "tier", StringComparison.OrdinalIgnoreCase))
        {
            var wanted = (f.EligibleTierIds ?? new List<int>()).Distinct().ToList();
            var known = await context.RewardTiers.Where(t => wanted.Contains(t.Id)).OrderBy(t => t.MinPoints).Select(t => t.Id).ToListAsync();
            if (known.Count == 0) return PromotionValidationResult.Fail("Tick at least one membership rank, or choose All guests.");
            tierIds = string.Join(",", known);
        }
        else if (string.Equals(f.Eligibility, "specific", StringComparison.OrdinalIgnoreCase))
        {
            var list = (f.EligibleEmails ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
            if (list.Count == 0) return PromotionValidationResult.Fail("Enter at least one guest email, or choose All guests.");
            var bad = list.FirstOrDefault(e => !EmailShape.IsMatch(e));
            if (bad != null) return PromotionValidationResult.Fail($"\"{bad}\" does not look like an email address.");
            emails = string.Join(", ", list.Select(e => e.ToLowerInvariant()).Distinct());
            if (emails.Length > 2000) return PromotionValidationResult.Fail("That is too many guests for one promotion. Use a category or a code instead.");
        }

        int? daysMask = null;
        var days = (f.AllowedDays ?? new List<int>()).Where(d => d is >= 0 and <= 6).Distinct().ToList();
        // All seven days (or none ticked) both mean "every day": store no restriction.
        if (days.Count > 0 && days.Count < 7) daysMask = days.Aggregate(0, (mask, d) => mask | (1 << d));

        return new PromotionValidationResult(true, string.Empty, title, description, percent, fixedAmount, code,
            f.StartDate.Value.Date, f.EndDate.Value.Date, itemId, categoryId, emails, tierIds, daysMask);
    }
}
