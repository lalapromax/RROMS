namespace RROMS.Presentation;

public static partial class Ui
{
    /// <summary>"Today, 09:10", "Yesterday", "16 Oct" or "16 Oct 2025": how long ago, in the way people say it.</summary>
    public static string When(DateTime local)
    {
        var today = DateTime.Today;
        var d = local.Date;
        if (d == today) return "Today, " + local.ToString("HH:mm");
        if (d == today.AddDays(-1)) return "Yesterday";
        return local.Year == today.Year ? local.ToString("d MMM") : local.ToString("d MMM yyyy");
    }
}
