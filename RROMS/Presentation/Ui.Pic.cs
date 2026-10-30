using System.Net;
using Microsoft.AspNetCore.Html;

namespace RROMS.Presentation;

public static partial class Ui
{
    /// 
    /// The stand-in picture shown wherever a dish, wine or set menu has no photo of its own, and in the decorative spots
    /// (home page, sign-in panel, tier cards, error card). One file, so it can be swapped for real photography later.
    /// The illustrated plates that used to be drawn here (SVG with blur and noise filters) cost too much memory.
    /// 
    public const string PlaceholderImage = "/static/assets/placeholder.png";

    /// 
    /// One guest-facing picture: the dish, set menu or wine's own photo, or the placeholder when it has none.
    /// 
    /// <param name="shape">sq, round, arch, soft or none (the radius the design uses for that spot).</param>
    /// <param name="surface">linen, oak, slate, olive, wine or cocoa; left empty the surface follows the dish.</param>
    public static IHtmlContent Pic(string? imageUrl, string? name, string? category = null, string shape = "sq",
        string? surface = null, string? cls = null, string? alt = null, bool decorative = false)
    {
        var surf = string.IsNullOrWhiteSpace(surface) ? Surface(name, category) : surface;
        var klass = $"img s-{surf} r-{shape}" + (string.IsNullOrWhiteSpace(cls) ? "" : " " + cls);
        var src = string.IsNullOrWhiteSpace(imageUrl) ? PlaceholderImage : imageUrl;
        var a = WebUtility.HtmlEncode(decorative ? "" : (alt ?? name ?? ""));
        return new HtmlString($"<div class=\"{klass}\"><img src=\"{WebUtility.HtmlEncode(src)}\" alt=\"{a}\" loading=\"lazy\" style=\"width:100%;height:100%;object-fit:cover;display:block\"></div>");
    }

    /// The placeholder in a decorative spot (no dish behind it), on the given surface and shape.
    public static IHtmlContent Placeholder(string surface, string shape = "sq", string? cls = null)
    {
        var klass = $"img s-{surface} r-{shape}" + (string.IsNullOrWhiteSpace(cls) ? "" : " " + cls);
        return new HtmlString($"<div class=\"{klass}\"><img src=\"{PlaceholderImage}\" alt=\"\" loading=\"lazy\" style=\"width:100%;height:100%;object-fit:cover;display:block\"></div>");
    }

    /// "Contains dairy, gluten" for the line under a dish; null when nothing is recorded.
    public static string? AllergenLine(string? allergens)
    {
        var list = RROMS.Services.DietaryRules.ParseList(allergens);
        if (list.Count == 0) return null;
        var names = list.OrderBy(a => a).Select(a => a.Equals("Milk", StringComparison.OrdinalIgnoreCase) ? "dairy" : a.ToLowerInvariant()
            .Replace("tree nuts", "nuts").Replace("eggs", "egg"));
        return "Contains " + string.Join(", ", names);
    }

    /// The background colour behind a dish's picture, picked from its name and category (wine dark, salads light...).
    public static string Surface(string? name, string? category)
    {
        var n = (name ?? "").ToLowerInvariant();
        var c = (category ?? "").ToLowerInvariant();
        bool Has(string s, params string[] keys) => keys.Any(s.Contains);

        var isWine = Has(c, "wine", "cellar", "champagne", "bubbl") || Has(n, "chablis", "champagne", "bordeaux", "burgundy", "merlot", "syrah", "rhône", "rhone", "cabernet", "pinot", "chardonnay", "sauvignon", "riesling", "prosecco", "crémant", "cremant", "brut", "rosé", "rose ");
        if (isWine)
        {
            if (Has(n, "champagne", "sparkling", "brut", "prosecco", "crémant", "cremant", "bubbl")) return "cocoa";
            if (Has(n, "chablis", "white", "blanc", "chardonnay", "sauvignon", "riesling", "rosé")) return "olive";
            return "wine";
        }
        if (Has(n, "glass of red", "house red")) return "wine";
        if (Has(n, "glass of white", "house white")) return "olive";
        if (Has(n, "tartare", "carpaccio", "crudo")) return "linen";
        if (Has(n, "consommé", "consomme", "broth", "bouillon")) return "wine";
        if (Has(n, "soup", "velouté", "veloute", "bisque", "potage", "chowder") || Has(c, "soup")) return "olive";
        if (Has(n, "chèvre", "chevre", "salad", "salade", "burrata") || Has(c, "salad")) return "linen";
        if (Has(n, "duck", "canard", "magret", "confit")) return "slate";
        if (Has(n, "sole", "fish", "salmon", "saumon", "cod", "bass", "turbot", "trout", "prawn", "shrimp", "scallop", "lobster", "oyster", "seafood") || Has(c, "seafood", "fish")) return "slate";
        if (Has(n, "gnocchi", "pasta", "risotto", "ravioli", "tagliatelle", "spaghetti", "noodle")) return "oak";
        if (Has(n, "tatin", "tart", "cake", "crème", "creme", "mousse", "soufflé", "souffle", "pudding", "macaron", "sorbet", "ice cream", "chocolate") || Has(c, "dessert", "sweet", "pastry")) return "linen";
        if (Has(n, "poulet", "chicken", "volaille", "coq", "beef", "bœuf", "boeuf", "steak", "lamb", "agneau", "pork", "veal", "filet", "fillet", "rib") || Has(c, "main", "grill", "meat")) return "oak";
        return "linen";
    }

    /// " err" when the model-state entry has an error, so the field gets the wine outline. Use inside class="in@(Ui.Err(...))".
    public static string Err(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary state, string key) =>
        state.TryGetValue(key, out var entry) && entry.Errors.Count > 0 ? " err" : "";

    /// "RM 118" for whole amounts, "RM 118.50" otherwise.
    public static string Rm(decimal amount) =>
        amount == decimal.Truncate(amount)
            ? $"{Brand.Currency} {amount:#,0}"
            : $"{Brand.Currency} {amount:#,0.00}";

    /// Always two decimals: receipts, bills and totals.
    public static string Rm2(decimal amount) => $"{Brand.Currency} {amount:#,0.00}";

    /// One outlined star. The star pickers draw five of these and fill them with CSS as a rating is chosen.
    public static IHtmlContent Star(int size = 30) =>
        new HtmlString($"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M12 2.8l2.9 6 6.6.9-4.8 4.6 1.2 6.5L12 17.7 6.1 20.8l1.2-6.5L2.5 9.7l6.6-.9z\"></path></svg>");

    /// Five outlined stars, <paramref name="n"/> of them filled.
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
