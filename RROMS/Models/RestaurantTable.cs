using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

public class RestaurantTable
{
    public int Id { get; set; }

    [Required, StringLength(20)]
    public string TableNumber { get; set; } = string.Empty;

    public int DiningAreaId { get; set; }
    public DiningArea? DiningArea { get; set; }

    [Range(1, 30)]
    public int Capacity { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();

    [NotMapped]
    public string DisplayName => $"{TableNumber} (seats {Capacity})";
}
