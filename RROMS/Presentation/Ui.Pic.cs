using System.Net;
using Microsoft.AspNetCore.Html;

namespace RROMS.Presentation;

public static partial class Ui
{
    /// <summary>
    /// One guest-facing picture. If the dish, set menu or wine has its own photo that is shown; otherwise the matching
    /// illustrated plate from the design is drawn, so a guest never sees an empty box. Staff may leave photos out.
    /// </summary>
    /// <param name="shape">sq, round, arch, soft or none (the radius the design uses for that spot).</param>
    /// <param name="surface">linen, oak, slate, olive, wine or cocoa; left empty the surface follows the picture.</param>
    public static IHtmlContent Pic(string? imageUrl, string? name, string? category = null, string shape = "sq",
        string? surface = null, string? cls = null, string? alt = null, bool decorative = false)
    {
        var (sym, surf) = Symbol(name, category);
        surf = string.IsNullOrWhiteSpace(surface) ? surf : surface;
        var klass = $"img s-{surf} r-{shape}" + (string.IsNullOrWhiteSpace(cls) ? "" : " " + cls);
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            var a = WebUtility.HtmlEncode(decorative ? "" : (alt ?? name ?? ""));
            return new HtmlString($"<div class=\"{klass}\"><img src=\"{WebUtility.HtmlEncode(imageUrl)}\" alt=\"{a}\" loading=\"lazy\" style=\"width:100%;height:100%;object-fit:cover;display:block\"></div>");
        }
        var label = decorative ? "aria-hidden=\"true\"" : $"role=\"img\" aria-label=\"{WebUtility.HtmlEncode(alt ?? name ?? "Illustrated plate")}\"";
        return new HtmlString($"<div class=\"{klass}\"><svg viewBox=\"0 0 200 200\" {label} focusable=\"false\"><use href=\"#{sym}\"></use></svg></div>");
    }

    /// <summary>A fixed illustrated plate from the design (used where the picture is decoration, not a specific dish).</summary>
    public static IHtmlContent Plate(string symbol, string surface, string shape = "sq", string? cls = null)
    {
        var klass = $"img s-{surface} r-{shape}" + (string.IsNullOrWhiteSpace(cls) ? "" : " " + cls);
        return new HtmlString($"<div class=\"{klass}\"><svg viewBox=\"0 0 200 200\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#{symbol}\"></use></svg></div>");
    }

    /// <summary>
    /// The 2 x 2 collage that stands for a set menu: the picture of the first dish of each of its first four courses.
    /// A set menu photo, when staff added one, replaces the collage.
    /// </summary>
    public static IHtmlContent SetCover(RROMS.Models.SetMenu menu, string? style = null, string shape = "none")
    {
        var sb = new System.Text.StringBuilder();
        var st = string.IsNullOrWhiteSpace(style) ? "" : $" style=\"{WebUtility.HtmlEncode(style)}\"";
        if (!string.IsNullOrWhiteSpace(menu.ImageUrl))
        {
            sb.Append($"<div class=\"cover\"{st}>");
            sb.Append(Pic(menu.ImageUrl, menu.Name, null, shape, "linen", "cover-photo", menu.Name).ToString());
            sb.Append("</div>");
            return new HtmlString(sb.ToString());
        }
        sb.Append($"<div class=\"cover\"{st} role=\"img\" aria-label=\"{WebUtility.HtmlEncode(menu.Name)}\">");
        var firsts = menu.Courses.OrderBy(c => c.SortOrder)
            .Select(c => c.Dishes.OrderBy(d => d.SortOrder).FirstOrDefault()?.MenuItem)
            .Where(m => m != null).Take(4).ToList();
        foreach (var m in firsts)
            sb.Append(Pic(m!.ImageUrl, m.Name, m.MenuCategory?.Name, shape, null, null, null, decorative: true).ToString());
        for (var i = firsts.Count; i < 4; i++)
            sb.Append(Plate("d-house", i % 2 == 0 ? "linen" : "olive", shape).ToString());
        sb.Append("</div>");
        return new HtmlString(sb.ToString());
    }

    /// <summary>"Contains dairy, gluten" for the line under a dish; null when nothing is recorded.</summary>
    public static string? AllergenLine(string? allergens)
    {
        var list = RROMS.Services.DietaryRules.ParseList(allergens);
        if (list.Count == 0) return null;
        var names = list.OrderBy(a => a).Select(a => a.Equals("Milk", StringComparison.OrdinalIgnoreCase) ? "dairy" : a.ToLowerInvariant()
            .Replace("tree nuts", "nuts").Replace("eggs", "egg"));
        return "Contains " + string.Join(", ", names);
    }

    /// <summary>Picks the illustrated plate for a dish by its name and category. Falls back to the plain house plate.</summary>
    public static (string Symbol, string Surface) Symbol(string? name, string? category)
    {
        var n = (name ?? "").ToLowerInvariant();
        var c = (category ?? "").ToLowerInvariant();
        bool Has(string s, params string[] keys) => keys.Any(s.Contains);

        var isWine = Has(c, "wine", "cellar", "champagne", "bubbl") || Has(n, "chablis", "champagne", "bordeaux", "burgundy", "merlot", "syrah", "rhône", "rhone", "cabernet", "pinot", "chardonnay", "sauvignon", "riesling", "prosecco", "crémant", "cremant", "brut", "rosé", "rose ");
        if (isWine)
        {
            if (Has(n, "champagne", "sparkling", "brut", "prosecco", "crémant", "cremant", "bubbl")) return ("b-champ", "cocoa");
            if (Has(n, "chablis", "white", "blanc", "chardonnay", "sauvignon", "riesling", "rosé")) return ("b-chablis", "olive");
            return ("b-saint", "wine");
        }
        if (Has(n, "glass of red", "house red")) return ("g-red", "wine");
        if (Has(n, "glass of white", "house white")) return ("g-white", "olive");
        if (Has(n, "tartare", "carpaccio", "crudo")) return ("d-tartare", "linen");
        if (Has(n, "consommé", "consomme", "broth", "bouillon")) return ("d-consomme", "wine");
        if (Has(n, "soup", "velouté", "veloute", "bisque", "potage", "chowder") || Has(c, "soup")) return ("d-soup", "olive");
        if (Has(n, "chèvre", "chevre", "salad", "salade", "burrata") || Has(c, "salad")) return ("d-chevre", "linen");
        if (Has(n, "duck", "canard", "magret", "confit")) return ("d-duck", "slate");
        if (Has(n, "sole", "fish", "salmon", "saumon", "cod", "bass", "turbot", "trout", "prawn", "shrimp", "scallop", "lobster", "oyster", "seafood") || Has(c, "seafood", "fish")) return ("d-sole", "slate");
        if (Has(n, "gnocchi", "pasta", "risotto", "ravioli", "tagliatelle", "spaghetti", "noodle")) return ("d-gnocchi", "oak");
        if (Has(n, "tatin", "tart", "cake", "crème", "creme", "mousse", "soufflé", "souffle", "pudding", "macaron", "sorbet", "ice cream", "chocolate") || Has(c, "dessert", "sweet", "pastry")) return ("d-tatin", "linen");
        if (Has(n, "poulet", "chicken", "volaille", "coq", "beef", "bœuf", "boeuf", "steak", "lamb", "agneau", "pork", "veal", "filet", "fillet", "rib") || Has(c, "main", "grill", "meat")) return ("d-poulet", "oak");
        return ("d-house", "linen");
    }

    /// <summary>" err" when the model-state entry has an error, so the field gets the wine outline. Use inside class="in@(Ui.Err(...))".</summary>
    public static string Err(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary state, string key) =>
        state.TryGetValue(key, out var entry) && entry.Errors.Count > 0 ? " err" : "";

    /// <summary>"RM 118" for whole amounts, "RM 118.50" otherwise.</summary>
    public static string Rm(decimal amount) =>
        amount == decimal.Truncate(amount)
            ? $"{Brand.Currency} {amount:#,0}"
            : $"{Brand.Currency} {amount:#,0.00}";

    /// <summary>Always two decimals: receipts, bills and totals.</summary>
    public static string Rm2(decimal amount) => $"{Brand.Currency} {amount:#,0.00}";

    /// <summary>One outlined star. The star pickers draw five of these and fill them with CSS as a rating is chosen.</summary>
    public static IHtmlContent Star(int size = 30) =>
        new HtmlString($"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M12 2.8l2.9 6 6.6.9-4.8 4.6 1.2 6.5L12 17.7 6.1 20.8l1.2-6.5L2.5 9.7l6.6-.9z\"></path></svg>");

    /// <summary>Five outlined stars, <paramref name="n"/> of them filled.</summary>
    public static IHtmlContent Stars(int n, int size = 16, string cls = "stars")
    {
        const string star = "M12 2.8l2.9 6 6.6.9-4.8 4.6 1.2 6.5L12 17.7 6.1 20.8l1.2-6.5L2.5 9.7l6.6-.9z";
        var sb = new System.Text.StringBuilder();
        sb.Append($"<span class=\"{cls}\" role=\"img\" aria-label=\"{n} out of 5 stars\">");
        for (var i = 0; i < 5; i++)
            sb.Append($"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"{(i < n ? "currentColor" : "none")}\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\" aria-hidden=\"true\"><path d=\"{star}\"></path></svg>");
        sb.Append("</span>");
        return new HtmlString(sb.ToString());
    }
}
