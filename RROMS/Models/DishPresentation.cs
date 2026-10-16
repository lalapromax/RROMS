namespace RROMS.Models;

/// 
/// Lightweight, dependency-free presentation helpers for the customer-facing menu.
/// No menu items have uploaded photography yet (see MenuItem.ImageUrl), so every
/// dish card needs an attractive fallback visual instead of a blank box - this
/// picks a meaningful emoji glyph from the dish's category so the browsing
/// experience still feels image-forward until real photos are uploaded via
/// Admin &gt; Menu Management.
/// 
public static class DishPresentation
{
    public static string CategoryEmoji(string? categoryName)
    {
        var n = (categoryName ?? string.Empty).ToLowerInvariant();

        if (n.Contains("starter") || n.Contains("appetiz") || n.Contains("small plate")) return "\U0001F961"; // dumpling
        if (n.Contains("soup")) return "\U0001F372"; // pot of food
        if (n.Contains("salad")) return "\U0001F957"; // salad
        if (n.Contains("pizza")) return "\U0001F355";
        if (n.Contains("pasta") || n.Contains("noodle")) return "\U0001F35D";
        if (n.Contains("seafood") || n.Contains("fish") || n.Contains("sushi")) return "\U0001F41F";
        if (n.Contains("burger") || n.Contains("sandwich")) return "\U0001F354";
        if (n.Contains("grill") || n.Contains("steak") || n.Contains("meat") || n.Contains("bbq")) return "\U0001F969";
        if (n.Contains("dessert") || n.Contains("sweet") || n.Contains("cake") || n.Contains("pastry")) return "\U0001F370";
        if (n.Contains("drink") || n.Contains("beverage") || n.Contains("bar") || n.Contains("cocktail")) return "\U0001F379";
        if (n.Contains("breakfast")) return "\U0001F373";
        if (n.Contains("side")) return "\U0001F35F";
        return "\U0001F37D️"; // generic plated meal
    }

    /// A short, meaningful glyph for the spicy indicator (a chilli, not a generic symbol).
    public const string SpicyEmoji = "\U0001F336️";

    /// A short, meaningful glyph for the vegetarian indicator.
    public const string VegetarianEmoji = "\U0001F331";
}
