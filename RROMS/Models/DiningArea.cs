using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

public class DiningArea
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<RestaurantTable> Tables { get; set; } = new List<RestaurantTable>();
}
