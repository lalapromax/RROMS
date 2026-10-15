namespace RROMS.Services;

/// 
/// Decides whether a configuration value was really filled in by a person.
/// appsettings.json ships with obvious placeholders such as "YOUR_GOOGLE_CLIENT_ID" so you can see
/// exactly where to paste each credential. A placeholder must count as "not set": otherwise the
/// app would try to call Google/Stripe/SMTP with a fake value and fail in confusing ways.
/// 
public static class IntegrationSettings
{
    private static readonly string[] PlaceholderMarkers = { "YOUR_", "PASTE_", "REPLACE_ME", "CHANGE_ME", "EXAMPLE_KEY" };

    public static bool IsReal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (v.StartsWith('<') && v.EndsWith('>')) return false;
        return !PlaceholderMarkers.Any(m => v.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// "set" / "not set" plus the last four characters, so an admin can tell which key is loaded
    /// without the full secret ever being rendered into a page.
    public static string Describe(string? value)
    {
        if (!IsReal(value)) return "not set";
        var v = value!.Trim();
        return v.Length <= 8 ? "set" : $"set (ends in {v[^4..]})";
    }
}
