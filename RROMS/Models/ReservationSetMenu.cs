using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

///
/// One set menu booked ahead with a reservation, for some or all of the table. A booking can carry several (for example
/// two guests on the tasting menu and two on the lunch menu). When the table is seated they are sent to the kitchen as an order.
///
public class ReservationSetMenu
{
    public int Id { get; set; }

    public int ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    /// Null once the set menu itself has been deleted; <see cref="SetMenuName"/> keeps the booking readable.
    public int? SetMenuId { get; set; }
    public SetMenu? SetMenu { get; set; }

    [Required, StringLength(80)]
    public string SetMenuName { get; set; } = string.Empty;

    /// How many of the party have this set menu.
    [Range(1, 30)]
    public int Guests { get; set; } = 1;

    /// When it was sent to the kitchen (the table was seated). Null until then, so seating twice never sends it twice.
    public DateTime? SentToKitchenAt { get; set; }
}
