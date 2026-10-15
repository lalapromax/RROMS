using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

public class RewardLedgerEntry
{
    public int Id { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int Points { get; set; }

    [StringLength(200)]
    public string Reason { get; set; } = string.Empty;

    public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
}
