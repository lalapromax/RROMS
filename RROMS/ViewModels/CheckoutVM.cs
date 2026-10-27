using System.ComponentModel.DataAnnotations;
using RROMS.Models;

namespace RROMS.ViewModels;

/// What the cart page posts. Whether the order is dine-in or for collection is never posted: it follows from the reservation.
public class CheckoutVM
{
    /// The booking this order is for. Left empty when the guest has no table booked.
    public int? ReservationId { get; set; }

    [StringLength(400)]
    public string? Notes { get; set; }

    [StringLength(30)]
    public string? PromoCode { get; set; }

    [Required]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.OnlinePayment;

    /// 
    /// Set by client-side script only after Stripe has confirmed the charge. Never trusted on its own:
    /// PlaceOrderAsync re-fetches this PaymentIntent from Stripe to verify it succeeded and for the right amount.
    /// 
    [StringLength(80)]
    public string? StripePaymentIntentId { get; set; }
}
