using System.ComponentModel.DataAnnotations;
using RROMS.Models;

namespace RROMS.ViewModels;

public class CheckoutVM
{
    [Required]
    public OrderType OrderType { get; set; } = OrderType.DineIn;

    /// <summary>An upcoming confirmed reservation to attach a dine-in order to.</summary>
    public int? ReservationId { get; set; }

    [StringLength(400)]
    public string? Notes { get; set; }

    [Required]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    // Mock card fields - never persisted, used only to simulate a card/online payment.
    [StringLength(40)]
    public string? CardName { get; set; }

    [StringLength(19)]
    public string? CardNumber { get; set; }

    [StringLength(5)]
    public string? CardExpiry { get; set; }
}
