using RROMS.Models;

namespace RROMS.Presentation;

/// Short phrases for a set menu card: when it is served and how many courses it has.
public static class SetMenuLabels
{
    private static readonly (DayOfWeek Day, string Short)[] Week =
    {
        (DayOfWeek.Monday, "Mon"), (DayOfWeek.Tuesday, "Tue"), (DayOfWeek.Wednesday, "Wed"), (DayOfWeek.Thursday, "Thu"),
        (DayOfWeek.Friday, "Fri"), (DayOfWeek.Saturday, "Sat"), (DayOfWeek.Sunday, "Sun")
    };

    public static string Service(SetMenu m) =>
        m.ServedAtLunch && m.ServedAtDinner ? "Lunch and dinner" : m.ServedAtLunch ? "Lunch" : "Dinner";

    /// "Every day", "Wed to Sun", "Tue, Thu, Sat".
    public static string Days(SetMenu m)
    {
        var on = Week.Select(w => m.OffersOn(w.Day)).ToArray();
        if (on.All(x => x)) return "every day";
        if (on.All(x => !x)) return "no days chosen";
        var parts = new List<string>();
        for (var i = 0; i < Week.Length; i++)
        {
            if (!on[i]) continue;
            var j = i;
            while (j + 1 < Week.Length && on[j + 1]) j++;
            var length = j - i + 1;
            parts.Add(length >= 3 ? $"{Week[i].Short} to {Week[j].Short}" : length == 2 ? $"{Week[i].Short}, {Week[j].Short}" : Week[i].Short);
            i = j;
        }
        return string.Join(", ", parts);
    }

    public static string Courses(SetMenu m) => m.Courses.Count == 1 ? "1 course" : $"{m.Courses.Count} courses";

    /// The picture for a card: the menu's own photo, else its main course's first dish, else the plain house plate.
    public static Microsoft.AspNetCore.Html.IHtmlContent Cover(SetMenu m)
    {
        if (!string.IsNullOrWhiteSpace(m.ImageUrl)) return Ui.Pic(m.ImageUrl, m.Name, null, "none", "linen", null, m.Name);
        var course = m.Courses.OrderBy(c => c.SortOrder).FirstOrDefault(c => c.Name.Contains("main", StringComparison.OrdinalIgnoreCase))
                     ?? m.Courses.OrderBy(c => c.SortOrder).FirstOrDefault();
        var dish = course?.Dishes.OrderBy(d => d.SortOrder).FirstOrDefault()?.MenuItem;
        return dish != null
            ? Ui.Pic(dish.ImageUrl, dish.Name, dish.MenuCategory?.Name, "none", null, null, null, decorative: true)
            : Ui.Plate("d-house", "linen", "none");
    }
}
