using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

/// 
/// A flexible, backend-validated discount. Scope is whichever one of
/// MenuItemId / MenuCategoryId is set - neither set means storewide. Every
/// other field below is an optional restriction: leaving it unset means "no
/// restriction" on that axis. See IPromotionService for how these are
/// combined and enforced - nothing here is ever trusted from the browser,
/// only configured by an administrator and evaluated fresh on the server
/// whenever a price is calculated or an order is placed.
/// 
public class Promotion
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Description { get; set; }

    /// Percent off the lines this promotion covers. 0 when the promotion is a fixed amount instead (<see cref="FixedAmountOff"/>).
    [Column(TypeName = "decimal(5,2)")]
    [Range(0, 100)]
    public decimal DiscountPercent { get; set; }

    /// A fixed amount off the whole order (for example RM 20 off orders over RM 300). Only used with a promo code.
    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 100000)]
    public decimal? FixedAmountOff { get; set; }

    /// 
    /// A code the guest types in the cart. A promotion with a code never applies by itself; one without a code applies
    /// automatically to the dishes it covers. Codes are matched ignoring upper and lower case.
    /// 
    [StringLength(30)]
    public string? Code { get; set; }

    /// Scope: this exact dish. Null means "not item-scoped" - see MenuCategoryId.
    public int? MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }

    /// Scope: every dish in this category. Null (with MenuItemId also null) means storewide.
    public int? MenuCategoryId { get; set; }
    public MenuCategory? MenuCategory { get; set; }

    /// First and last day the promotion runs, as calendar days in the restaurant's own time (like bookings), never UTC.
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddDays(7);

    public bool IsActive { get; set; } = true;

    /// Bitmask of allowed System.DayOfWeek values (bit set at 1 &lt;&lt;
    /// (int)DayOfWeek). Null means every day of the week is allowed - this is
    /// what lets an admin set up something like "Mondays only".
    public int? AllowedDaysMask { get; set; }

    /// Comma/semicolon/newline-separated list of specific eligible customer
    /// emails. Null or blank means open to every customer.
    [StringLength(2000)]
    public string? EligibleEmailsRaw { get; set; }

    /// Comma-separated RewardTier ids (membership ranks such as Silver and Gold) whose members may use this promotion.
    /// Null or blank means no rank restriction. A guest's rank comes from their lifetime points at the moment they order.
    [StringLength(200)]
    public string? EligibleTierIdsRaw { get; set; }

    /// Cart subtotal (before any discount) that must be reached for this
    /// promotion to apply. Null means no minimum.
    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 100000)]
    public decimal? MinimumOrderAmount { get; set; }

    /// Caps how many orders in total may ever use this promotion. Null means unlimited.
    [Range(1, int.MaxValue)]
    public int? MaxRedemptionsTotal { get; set; }

    /// Caps how many orders a single customer may use this promotion for. Null means unlimited.
    [Range(1, int.MaxValue)]
    public int? MaxRedemptionsPerCustomer { get; set; }

    public ICollection<PromotionRedemption> Redemptions { get; set; } = new List<PromotionRedemption>();

    [NotMapped]
    public bool IsCurrentlyRunning => IsActive && DateTime.Today >= StartDate.Date && DateTime.Today <= EndDate.Date;

    [NotMapped]
    public bool IsEligibilityRestricted => !string.IsNullOrWhiteSpace(EligibleEmailsRaw);

    [NotMapped]
    public bool IsTierRestricted => !string.IsNullOrWhiteSpace(EligibleTierIdsRaw);

    public HashSet<int> GetEligibleTierIds()
    {
        if (string.IsNullOrWhiteSpace(EligibleTierIdsRaw)) return new HashSet<int>();
        return EligibleTierIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();
    }

    /// Parses <see cref="EligibleEmailsRaw"/> into a case-insensitive set of
    /// trimmed, lower-cased emails. Accepts commas, semicolons or newlines as separators.
    public HashSet<string> GetEligibleEmails()
    {
        if (string.IsNullOrWhiteSpace(EligibleEmailsRaw)) return new HashSet<string>();
        return EligibleEmailsRaw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length > 0)
            .ToHashSet();
    }

    public bool IsDayAllowed(DayOfWeek day) => AllowedDaysMask == null || (AllowedDaysMask.Value & (1 << (int)day)) != 0;

    /// Human-readable scope label for admin listings - "Storewide", a category
    /// name, or a dish name, depending on which scope field is set.
    [NotMapped]
    public string ScopeLabel => MenuItem != null ? $"Item: {MenuItem.Name}" : MenuCategory != null ? $"Category: {MenuCategory.Name}" : "Storewide";
}
