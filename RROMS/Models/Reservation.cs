using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

public class Reservation
{
    public int Id { get; set; }

    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    public int? TableId { get; set; }
    public RestaurantTable? Table { get; set; }

    [Range(1, 30)]
    public int PartySize { get; set; }

    /// Combined booking date + time, stored in local restaurant time.
    public DateTime ReservationDateTime { get; set; }

    public int DurationMinutes { get; set; } = 90;

    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    [StringLength(400)]
    public string? SpecialRequests { get; set; }

    [StringLength(40)]
    public string? Occasion { get; set; }

    /// Optional: the set menus chosen when booking, each for some of the table.
    public ICollection<ReservationSetMenu> SetMenus { get; set; } = new List<ReservationSetMenu>();

    /// Who to ask for at the door, if different from the account name, and a number to reach them on the day.
    [StringLength(80)]
    public string? ContactName { get; set; }

    [StringLength(30)]
    public string? ContactPhone { get; set; }

    [StringLength(300)]
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }

    public string? ConfirmedByStaffId { get; set; }
    public ApplicationUser? ConfirmedByStaff { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();

    /// Guests may change or cancel on their own until this many hours before the booking; after that they call us.
    public const int GuestChangeCutoffHours = 4;

    [NotMapped]
    public bool GuestCanChange =>
        Status is ReservationStatus.Pending or ReservationStatus.Confirmed
        && ReservationDateTime > DateTime.Now.AddHours(GuestChangeCutoffHours);

    [NotMapped]
    public DateTime ReservationEndDateTime => ReservationDateTime.AddMinutes(DurationMinutes);
}
