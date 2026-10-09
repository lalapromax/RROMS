namespace RROMS.ViewModels;

public enum CartKind
{
    Dish = 0,
    WineGlass = 1,
    WineBottle = 2,
    SetMenu = 3
}

/// <summary>One dish chosen inside a set menu line (course name and dish name are kept for the cart's summary only).</summary>
public class CartPickVM
{
    public int CourseId { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public int MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Supplement { get; set; }
}

/// <summary>
/// A single line in the cart. The whole cart is serialized to JSON and kept in the customer's session between
/// "Add to order" and checkout. Prices here are only for showing the cart: checkout prices every line again from the database.
/// For a set menu, <see cref="Quantity"/> is the number of guests who take exactly these choices.
/// </summary>
public class CartItemVM
{
    public CartKind Kind { get; set; } = CartKind.Dish;

    /// <summary>The dish or wine. 0 for a set menu line (its dishes are in <see cref="Picks"/>).</summary>
    public int MenuItemId { get; set; }

    public int? SetMenuId { get; set; }
    public List<CartPickVM> Picks { get; set; } = new();

    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? SpecialInstructions { get; set; }

    /// <summary>Snapshot of the dish's photo at add-to-cart time, for the cart's thumbnail only (display, not pricing).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Category name at add time, for the line under the name ("Main", "Cellar").</summary>
    public string? CategoryName { get; set; }

    public decimal Subtotal => UnitPrice * Quantity;

    /// <summary>Identifies a line for +, - and Remove: two lines are the same line only when they hold the same thing.</summary>
    public string Key => Kind == CartKind.SetMenu
        ? $"s{SetMenuId}:" + string.Join(",", Picks.OrderBy(p => p.CourseId).ThenBy(p => p.MenuItemId).Select(p => $"{p.CourseId}-{p.MenuItemId}"))
        : $"{(int)Kind}:{MenuItemId}";
}
