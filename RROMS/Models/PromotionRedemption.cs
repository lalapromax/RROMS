using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

/// 
/// One row per (order, promotion) pair where that promotion actually reduced
/// the price a customer paid. This is the source of truth for enforcing
/// Promotion.MaxRedemptionsTotal / MaxRedemptionsPerCustomer - those limits
/// are only meaningful if counted from rows written at order-placement time
/// on the server, never from anything the browser reports back.
/// 
public class PromotionRedemption
{
    public int Id { get; set; }

    public int PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal DiscountAmount { get; set; }

    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
}
