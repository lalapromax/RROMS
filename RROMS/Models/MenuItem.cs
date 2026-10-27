


using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

public class MenuItem
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 10000)]
    public decimal Price { get; set; }

    public int MenuCategoryId { get; set; }
    public MenuCategory? MenuCategory { get; set; }

    [StringLength(300)]
    public string? ImageUrl { get; set; }

    public bool IsAvailable { get; set; } = true;
    public bool IsVegetarian { get; set; }
    public bool IsSpicy { get; set; }

    /// Shown first on the menu with the handwritten "chef's pick" tag. Staff choose which dishes carry it.
    public bool IsChefsPick { get; set; }
    public int? Calories { get; set; }

    /// Wine only: the price of one glass. <see cref="Price"/> is then the price of the bottle. Null means the wine is sold by the bottle only.
    [Column(TypeName = "decimal(10,2)")]
    [Range(0, 10000)]
    public decimal? GlassPrice { get; set; }

    /// Wine only: the line under the name, for example "Burgundy, white".
    [StringLength(80)]
    public string? WineNote { get; set; }

    /// 
    /// Comma-separated canonical allergen names this dish CONTAINS (see DietaryRules.AllergenNames),
    /// entered by the restaurant. Null means "not recorded" and is treated cautiously by the
    /// dietary filters rather than as "allergen-free".
    /// 
    [StringLength(200)]
    public string? Allergens { get; set; }

    /// 
    /// Comma-separated diet labels this dish SATISFIES (see DietaryRules.DietTags),
    /// e.g. "Vegan,Gluten-Free". Vegetarian is the separate IsVegetarian flag.
    /// 
    [StringLength(200)]
    public string? DietaryTags { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();
}
