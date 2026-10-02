using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

/// <summary>
/// Single-row table holding restaurant-wide configuration, managed from Admin > Settings.
/// </summary>
public class RestaurantSetting
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string RestaurantName { get; set; } = "RROMS Restaurant";

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(120)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Default length of a table booking, used by the availability engine.</summary>
    public int ReservationDurationMinutes { get; set; } = 90;

    /// <summary>How many days in advance a customer may book.</summary>
    public int MaxAdvanceBookingDays { get; set; } = 60;

    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(5,2)")]
    public decimal TaxRatePercent { get; set; } = 6.0m;
}
