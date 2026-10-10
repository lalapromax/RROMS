using System.ComponentModel.DataAnnotations;

namespace RROMS.Models;

public class MenuCategory
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// True for the wine list. (Cellar) items in it carry a glass price and a bottle price.
    public bool IsWine { get; set; }

    /// True for drinks and extras that are poured only as part of a set menu (for example "a glass of the house red").
    /// They are never listed on the carte or in the cellar and cannot be ordered on their own.
    public bool SetMenuOnly { get; set; }

    public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
}
