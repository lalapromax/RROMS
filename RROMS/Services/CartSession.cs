using System.Text.Json;
using RROMS.ViewModels;

namespace RROMS.Services;

/// <summary>Reads and writes the cart kept in the session. One place, so the menu, the cart and the top bar agree on what is in it.</summary>
public static class CartSession
{
    private const string Key = "Cart";

    public static List<CartItemVM> Load(ISession session)
    {
        var json = session.GetString(Key);
        if (string.IsNullOrEmpty(json)) return new List<CartItemVM>();
        try { return JsonSerializer.Deserialize<List<CartItemVM>>(json) ?? new List<CartItemVM>(); }
        catch (JsonException) { return new List<CartItemVM>(); }
    }

    public static void Save(ISession session, List<CartItemVM> cart) => session.SetString(Key, JsonSerializer.Serialize(cart));

    public static void Clear(ISession session) => session.Remove(Key);

    /// <summary>Adds a line, or one more of an identical line.</summary>
    public static void Add(List<CartItemVM> cart, CartItemVM line)
    {
        var existing = cart.FirstOrDefault(c => c.Key == line.Key && c.SpecialInstructions == line.SpecialInstructions);
        if (existing != null) existing.Quantity = Math.Min(50, existing.Quantity + line.Quantity);
        else cart.Add(line);
    }
}
