using RROMS.Models;

namespace RROMS.Presentation;

/// <summary>One line on an order as the guest reads it. A set menu is one line however many dishes were chosen.</summary>
public sealed record OrderLineView(string Title, string Sub, string? ImageUrl, string? PicName, string? Category,
    int Quantity, decimal Amount, decimal Saved, bool IsSet);

/// <summary>
/// How an order is worded and drawn on My orders, the receipt, the Billing search and the staff order cards.
/// Kept in one place so a set menu, a glass of wine or a cancelled order reads the same everywhere.
/// </summary>
public static class OrderView
{
    /// <summary>cancelled, paid or unpaid. A cancelled order is never "paid" even if it was refunded.</summary>
    public static string Pay(Order o) =>
        o.Status == OrderStatus.Cancelled ? "cancelled"
        : o.Payment?.Status == PaymentStatus.Paid ? "paid"
        : "unpaid";

    public static (string Css, string Text) Pill(Order o) => Pay(o) switch
    {
        "cancelled" => ("st-cancelled", "Cancelled"),
        "paid" => ("st-paid", "Paid"),
        _ => ("st-unpaid", "Unpaid")
    };

    /// <summary>"Glass of House red", "Bottle of Chablis" or just the dish name.</summary>
    public static string Name(OrderItem i, bool lower = false)
    {
        var n = i.ItemNameSnapshot;
        return i.Serving switch
        {
            "Glass" => (lower ? "glass of " : "Glass of ") + n,
            "Bottle" => (lower ? "bottle of " : "Bottle of ") + n,
            _ => n
        };
    }

    /// <summary>The lines of an order: every ordinary item on its own, and each set menu folded into one line listing its dishes.</summary>
    public static List<OrderLineView> Lines(Order o)
    {
        var result = new List<OrderLineView>();
        var ordered = o.OrderItems.OrderBy(i => i.Id).ToList();
        var sets = ordered.Where(i => i.SetMenuGroup != null).GroupBy(i => i.SetMenuGroup!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var done = new HashSet<int>();

        foreach (var i in ordered)
        {
            if (i.SetMenuGroup is int g)
            {
                if (!done.Add(g)) continue;
                var dishes = sets[g];
                var first = dishes[0];
                var courses = string.Join(" · ", dishes.Select(d => string.IsNullOrWhiteSpace(d.CourseName) ? d.ItemNameSnapshot : d.CourseName + ": " + d.ItemNameSnapshot));
                result.Add(new OrderLineView(first.SetMenuName ?? "Set menu", courses,
                    first.MenuItem?.ImageUrl, first.ItemNameSnapshot, first.MenuItem?.MenuCategory?.Name,
                    first.Quantity, dishes.Sum(d => d.Subtotal), dishes.Sum(d => d.DiscountAmount), true));
                continue;
            }

            var sub = i.Serving switch
            {
                "Glass" => "Cellar · by the glass",
                "Bottle" => "Cellar · bottle",
                _ => i.MenuItem?.MenuCategory?.Name ?? ""
            };
            if (!string.IsNullOrWhiteSpace(i.SpecialInstructions))
                sub = string.IsNullOrEmpty(sub) ? i.SpecialInstructions! : sub + ", " + i.SpecialInstructions!.ToLowerInvariant();
            result.Add(new OrderLineView(Name(i), sub, i.MenuItem?.ImageUrl, i.ItemNameSnapshot, i.MenuItem?.MenuCategory?.Name,
                i.Quantity, i.Subtotal, i.DiscountAmount, false));
        }
        return result;
    }

    /// <summary>"Tarte Tatin, Magret de canard ×2, glass of House red ×2". Set menus show by name.</summary>
    public static string Summary(Order o)
    {
        var parts = Lines(o).Select(l => (l.IsSet ? l.Title : l.Title.StartsWith("Glass of ") || l.Title.StartsWith("Bottle of ") ? char.ToLowerInvariant(l.Title[0]) + l.Title[1..] : l.Title)
                                         + (l.Quantity > 1 ? " ×" + l.Quantity : ""));
        return string.Join(", ", parts);
    }

    /// <summary>Up to three different pictures for the overlapping circles on My orders.</summary>
    public static List<OrderItem> Thumbs(Order o) =>
        o.OrderItems.OrderBy(i => i.Id).GroupBy(i => i.MenuItemId).Select(g => g.First()).Take(3).ToList();

    public static string Words(int n) => n switch
    {
        1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight",
        _ => n.ToString()
    };

    public static string Method(Payment? p)
    {
        if (p == null) return "Pay at the restaurant";
        if (p.Status != PaymentStatus.Paid && p.Status != PaymentStatus.Refunded) return "Pay at the restaurant";
        return p.Method switch
        {
            PaymentMethod.OnlinePayment => "Card, paid online",
            PaymentMethod.OnlineBanking => "Online banking",
            PaymentMethod.Card => "Card, at the restaurant",
            PaymentMethod.QrPayment => "QR payment, at the restaurant",
            _ => "Cash, at the restaurant"
        };
    }
}
