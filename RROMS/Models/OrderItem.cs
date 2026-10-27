using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RROMS.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }

    [Required, StringLength(100)]
    public string ItemNameSnapshot { get; set; } = string.Empty;

    [Range(1, 50)]
    public int Quantity { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Subtotal { get; set; }

    /// The promotion applied to this line, if any - recorded for transparency
    /// and so staff/admin can see exactly why a line was discounted.
    public int? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    /// How much this line was discounted by (0 if no promotion applied).
    /// Subtotal above is already net of this - UnitPrice * Quantity - DiscountAmount.
    [Column(TypeName = "decimal(10,2)")]
    public decimal DiscountAmount { get; set; }

    [StringLength(200)]
    public string? SpecialInstructions { get; set; }

    /// "Glass" or "Bottle" for wine lines; null for everything else.
    [StringLength(10)]
    public string? Serving { get; set; }

    /// 
    /// When the guest ordered a set menu, each dish they chose becomes its own line (so the kitchen sees every dish),
    /// all sharing the same <see cref="SetMenuGroup"/> number. The set price is spread over those lines, so the order
    /// total, the reports and the receipt all add up. Null for ordinary lines.
    /// 
    public int? SetMenuId { get; set; }

    [StringLength(100)]
    public string? SetMenuName { get; set; }

    public int? SetMenuGroup { get; set; }

    [StringLength(40)]
    public string? CourseName { get; set; }
}
