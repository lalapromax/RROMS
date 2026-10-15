using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// Bronze / Silver / Gold tiers. 
/// Rank is based on lifetime points. Not saved on the customer.
public class RewardTier
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string Name { get; set; } = string.Empty;

    /// Lifetime points needed to reach this tier. The lowest tier must start at 0.
    public int MinPoints { get; set; }

    [StringLength(200)]
    public string? Blurb { get; set; }

    /// Accent colour as #RRGGBB, used for the tier badge. Validated server-side before saving.
    [StringLength(7)]
    public string ColorHex { get; set; } = "#6C7F8E";

    /// Shown on the tier card once the guest has gone beyond this tier.
    [StringLength(200)]
    public string? MessagePassed { get; set; }

    /// Shown on the tier card while the guest is in this tier.
    [StringLength(200)]
    public string? MessageCurrent { get; set; }

    /// Shown on the tier card before the guest gets there.
    [StringLength(200)]
    public string? MessageLocked { get; set; }

    public ICollection<RewardPerk> Perks { get; set; } = new List<RewardPerk>();
}
