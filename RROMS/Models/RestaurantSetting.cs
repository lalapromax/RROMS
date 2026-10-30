using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// 
/// Single-row table holding restaurant-wide configuration, managed from Admin > Settings.
/// 
public class RestaurantSetting
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string RestaurantName { get; set; } = "RROMS Restaurant";

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    /// The number to show guests, or null while it is still the "[YOUR PHONE]" placeholder from the seed.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? PublicPhone => PublicPhoneOf(Phone);

    /// The same rule for a number held elsewhere (the error page reads it from the cache, not the database).
    public static string? PublicPhoneOf(string? phone) =>
        string.IsNullOrWhiteSpace(phone) || phone.Contains('[') ? null : phone.Trim();

    [StringLength(120)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    /// Default length of a table booking, used by the availability engine.
    public int ReservationDurationMinutes { get; set; } = 90;

    /// How many days in advance a customer may book.
    public int MaxAdvanceBookingDays { get; set; } = 60;

    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(5,2)")]
    public decimal TaxRatePercent { get; set; } = 6.0m;

    /// Added to dine-in bills before tax. 0 means no service charge.
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(5,2)")]
    public decimal ServiceChargePercent { get; set; } = 10.0m;

    /// How long a table is held after the booking time before it is released.
    public int HoldMinutes { get; set; } = 15;

    /// Rank guardrail: show a "you are ahead of N%" figure only to guests in the top half.
    public bool RankPercentTopHalfOnly { get; set; } = true;

    /// Rank guardrail: show a rank number (for example 7th) only to guests in the top 10%.
    public bool RankNumberTopTenOnly { get; set; } = true;
}
