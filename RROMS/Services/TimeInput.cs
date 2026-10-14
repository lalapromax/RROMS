namespace RROMS.Services;


public static class TimeInput
{
    public static DateTime? LocalToUtc(DateTime? local)
    {
        if (!local.HasValue) return null;
        var unspecified = DateTime.SpecifyKind(local.Value, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
    }

    /// Formats a stored UTC time for a datetime-local input (yyyy-MM-ddTHH:mm in local time), or null.
    public static string? ToInputValue(DateTime? utc)
    {
        if (!utc.HasValue) return null;
        var asUtc = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, TimeZoneInfo.Local).ToString("yyyy-MM-ddTHH:mm");
    }

    public static string Display(DateTime? utc, string format = "dd MMM yyyy HH:mm")
    {
        if (!utc.HasValue) return string.Empty;
        var asUtc = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, TimeZoneInfo.Local).ToString(format);
    }
}
