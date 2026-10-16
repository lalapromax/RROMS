using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

public enum RewardClaimStatus
{
    Waiting = 0,
    Fulfilled = 1,
    Declined = 2
}

/// 
/// A customer asking for a perk. The perk title and the customer's tier are copied onto the claim, so the
/// history stays readable even if an administrator later renames or deletes the perk.
/// 
public class RewardClaim
{
    public int Id { get; set; }

    public int? RewardPerkId { get; set; }
    public RewardPerk? Perk { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    [Required, StringLength(80)]
    public string PerkTitle { get; set; } = string.Empty;

    [StringLength(40)]
    public string TierName { get; set; } = string.Empty;

    public RewardClaimStatus Status { get; set; } = RewardClaimStatus.Waiting;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    public string? ResolvedById { get; set; }

    /// The administrator's note to the customer (for example why a claim was declined).
    [StringLength(300)]
    public string? AdminNote { get; set; }
}
