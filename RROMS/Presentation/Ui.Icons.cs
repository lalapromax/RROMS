using Microsoft.AspNetCore.Html;

namespace RROMS.Presentation;

/// 
/// The line icons used across the site, copied from the approved design. They are inline SVG so they take the
/// colour of the surrounding text and need no icon font or network request.
/// 
public static partial class Ui
{
    private static readonly Dictionary<string, string> IconPaths = new()
    {
        ["arrow"] = "<path d=\"M5 12h14M13 6l6 6-6 6\"></path>",
        ["bag"] = "<path d=\"M5 8h14l-1 12H6z\"></path><path d=\"M9 8V6a3 3 0 0 1 6 0v2\"></path>",
        ["bell"] = "<path d=\"M6 17V11a6 6 0 0 1 12 0v6l1.5 2h-15z\"></path><path d=\"M10 21a2 2 0 0 0 4 0\"></path>",
        ["book"] = "<path d=\"M5 4.5h10a3 3 0 0 1 3 3V20H8a3 3 0 0 1-3-3z\"></path><path d=\"M5 17a3 3 0 0 1 3-3h10\"></path>",
        ["bottle"] = "<path d=\"M10 3h4v4l2 3v10a1.5 1.5 0 0 1-1.5 1.5h-5A1.5 1.5 0 0 1 8 20V10l2-3z\"></path><path d=\"M8 14h8\"></path>",
        ["burger"] = "<path d=\"M4 7h16M4 12h16M4 17h16\"></path>",
        ["cake"] = "<path d=\"M4 20h16v-6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2z\"></path><path d=\"M4 16c2 1.5 3 1.5 4 0s2-1.5 4 0 3 1.5 4 0 2-1.5 4 0\"></path><path d=\"M12 12V8M12 5.5v.1\"></path>",
        ["cal"] = "<rect x=\"4\" y=\"5\" width=\"16\" height=\"15\" rx=\"2\"></rect><path d=\"M4 10h16M9 3v4M15 3v4\"></path>",
        ["card"] = "<rect x=\"3\" y=\"6\" width=\"18\" height=\"12\" rx=\"2\"></rect><path d=\"M3 10.5h18\"></path>",
        ["cart"] = "<path d=\"M3 4h2.6l2.2 11h10.3l2-8H6.4\"></path><circle cx=\"9\" cy=\"19.2\" r=\"1.3\"></circle><circle cx=\"17\" cy=\"19.2\" r=\"1.3\"></circle>",
        ["chat"] = "<path d=\"M4 5h16v11H9l-5 4z\"></path>",
        ["check"] = "<path d=\"M5 12.5l4.5 4.5L19 7.5\"></path>",
        ["chev"] = "<path d=\"M6 9l6 6 6-6\"></path>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"></circle><path d=\"M12 7.5V12l3 2\"></path>",
        ["crown"] = "<path d=\"M3.5 8l4.3 4 4.2-7 4.2 7 4.3-4-1.7 10.5H5.2z\"></path>",
        ["dl"] = "<path d=\"M12 4v11M7 11l5 5 5-5M5 20h14\"></path>",
        ["down"] = "<path d=\"M12 5v14M5 12l7 7 7-7\"></path>",
        ["eye"] = "<path d=\"M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z\"></path><circle cx=\"12\" cy=\"12\" r=\"2.8\"></circle>",
        ["flag"] = "<path d=\"M5 21V4M5 4h11l-2 4 2 4H5\"></path>",
        ["fork"] = "<path d=\"M7 3v8a2 2 0 0 0 4 0V3M9 3v18M17 3c-2 2-2.5 5-2.5 8h2.5v10\"></path>",
        ["gift"] = "<rect x=\"3.5\" y=\"9\" width=\"17\" height=\"11\" rx=\"1.5\"></rect><path d=\"M12 9v11M3.5 13h17M12 9c-1.5-4-6-4-5 0M12 9c1.5-4 6-4 5 0\"></path>",
        ["glass"] = "<path d=\"M7 3h10l-.7 7a4.3 4.3 0 0 1-8.6 0z\"></path><path d=\"M12 14.3V21M8.5 21h7\"></path>",
        ["globe"] = "<circle cx=\"12\" cy=\"12\" r=\"8.5\"></circle><path d=\"M3.5 12h17M12 3.5c3 3.5 3 13.5 0 17M12 3.5c-3 3.5-3 13.5 0 17\"></path>",
        ["hist"] = "<path d=\"M4 12a8 8 0 1 0 3-6.2\"></path><path d=\"M4 4v4h4M12 8v4l3 2\"></path>",
        ["link"] = "<path d=\"M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3A4 4 0 0 0 11 18.7l1-1\"></path>",
        ["lock"] = "<rect x=\"5\" y=\"11\" width=\"14\" height=\"9\" rx=\"2\"></rect><path d=\"M8 11V8a4 4 0 0 1 8 0v3\"></path>",
        ["mail"] = "<rect x=\"3\" y=\"5.5\" width=\"18\" height=\"13\" rx=\"2\"></rect><path d=\"M3.5 7l8.5 6 8.5-6\"></path>",
        ["move"] = "<path d=\"M12 3v18M3 12h18M12 3l-3 3M12 3l3 3M12 21l-3-3M12 21l3-3M3 12l3-3M3 12l3 3M21 12l-3-3M21 12l-3 3\"></path>",
        ["pen"] = "<path d=\"M4 20l1-4L16.5 4.5a2 2 0 0 1 3 3L8 19z\"></path>",
        ["play"] = "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"3\"></rect><path d=\"M10 9.5v5l4.5-2.5z\"></path>",
        ["plus"] = "<path d=\"M12 5v14M5 12h14\"></path>",
        ["receipt"] = "<path d=\"M6 3h12v18l-3-2-3 2-3-2-3 2z\"></path><path d=\"M9 8h6M9 12h6\"></path>",
        ["search"] = "<circle cx=\"11\" cy=\"11\" r=\"6.5\"></circle><path d=\"M16 16l4 4\"></path>",
        ["star"] = "<path d=\"M12 3.5l2.6 5.4 5.9.8-4.3 4.1 1 5.9L12 16.9 6.8 19.7l1-5.9L3.5 9.7l5.9-.8z\"></path>",
        ["table"] = "<path d=\"M4 9h16M6 9l-1 10M18 9l1 10M12 9v10\"></path><path d=\"M3 9c0-2 4-3 9-3s9 1 9 3\"></path>",
        ["tag"] = "<path d=\"M3.5 12.5V4.5h8l9 9-8 8z\"></path><circle cx=\"8\" cy=\"9\" r=\"1.2\"></circle>",
        ["trash"] = "<path d=\"M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13\"></path>",
        ["up"] = "<path d=\"M12 19V5M5 12l7-7 7 7\"></path>",
        ["user"] = "<circle cx=\"12\" cy=\"8\" r=\"4\"></circle><path d=\"M4 21c1-4.5 4.5-6.5 8-6.5s7 2 8 6.5\"></path>",
        ["warn"] = "<path d=\"M12 4l9 16H3z\"></path><path d=\"M12 10v4M12 17v.1\"></path>",
        ["x"] = "<path d=\"M5 5l14 14M19 5L5 19\"></path>",
    };

    public static bool HasIcon(string name) => IconPaths.ContainsKey(name);

    /// One inline icon. <paramref name="size"/> is in pixels; the stroke width follows the design (1.6).
    public static IHtmlContent Icon(string name, int size = 20, double stroke = 1.6)
    {
        if (!IconPaths.TryGetValue(name, out var d))
            return HtmlString.Empty;
        return new HtmlString(
            $"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
            $"stroke-width=\"{stroke.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{d}</svg>");
    }
}
