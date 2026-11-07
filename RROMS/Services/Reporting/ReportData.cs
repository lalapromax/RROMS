using System.Globalization;
using RROMS.Presentation;
using RROMS.ViewModels;

namespace RROMS.Services.Reporting;

/// <summary>Short, consistent ways to write money, dates and percentages in every export.</summary>
internal static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Rm(decimal v) => Brand.Currency + " " + Math.Round(v, MidpointRounding.AwayFromZero).ToString("#,##0", Inv);
    public static string Rm2(decimal v) => Brand.Currency + " " + v.ToString("#,##0.00", Inv);
    public static string Int(int v) => v.ToString("#,##0", Inv);
    public static string Num1(double v) => v.ToString("0.0", Inv);
    public static string Date(DateTime d) => d.ToString("d MMM yyyy", Inv);
    public static string Short(DateTime d) => d.ToString("d MMM", Inv);
    public static string Day(DateTime d) => d.ToString("ddd d MMM", Inv);
    public static string DateTimeText(DateTime d) => d.ToString("d MMM yyyy, HH:mm", Inv);
    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", Inv);
    public static string Pct(double fraction) => Math.Round(fraction * 100).ToString("0", Inv) + "%";
    public static string Hour(int h) => h.ToString("00", Inv) + ":00";
    public static string Signed(decimal v) => (v > 0 ? "+" : "") + v.ToString("0.#", Inv);

    public static string Period(DateTime a, DateTime b) =>
        a.Date == b.Date ? Date(a)
        : a.Year == b.Year ? Short(a) + " to " + Date(b)
        : Date(a) + " to " + Date(b);

    public static string Plural(int n, string one, string many) => n == 1 ? one : many;
}

internal sealed class CategoryShare
{
    public string Name { get; set; } = "";
    public int Items { get; set; }
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
    public double Share { get; set; }
}

internal sealed class WeekdayShare
{
    public DayOfWeek Day { get; set; }
    public int DaysInPeriod { get; set; }
    public int Orders { get; set; }
    public decimal Revenue { get; set; }
    public decimal AverageRevenue => DaysInPeriod == 0 ? 0 : Revenue / DaysInPeriod;
}

internal sealed class StatusRow
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double Share { get; set; }
}

/// <summary>
/// Everything the three downloads (PDF, Excel, CSV) draw on, gathered once so all three show the same numbers.
/// The figures come from the same report service as the Overview page; the extra breakdowns (weekday, menu category,
/// running total) are worked out here from those figures.
/// </summary>
internal sealed class ReportData
{
    public string Restaurant => Brand.Name;
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public int Days => (int)(End.Date - Start.Date).TotalDays + 1;

    public DashboardVM Overview { get; private set; } = new();
    public SalesReportVM Sales => Overview.Sales;
    public ReservationStatsVM Reservations => Overview.ReservationStats;
    public FeedbackSummaryVM Feedback => Overview.Feedback;

    public List<DailySalesPointVM> Daily { get; private set; } = new();
    public List<HourCountVM> Hours { get; private set; } = new();
    public List<PopularItemVM> Popular { get; private set; } = new();
    public List<CustomerActivityVM> Customers { get; private set; } = new();
    public List<RecentReviewVM> Reviews { get; private set; } = new();
    public List<RecentActivityItemVM> Activity => Overview.RecentActivity;

    public List<CategoryShare> Categories { get; private set; } = new();
    public List<WeekdayShare> Weekdays { get; private set; } = new();
    public List<StatusRow> Statuses { get; private set; } = new();

    public DailySalesPointVM? BestDay { get; private set; }
    public DailySalesPointVM? QuietestDay { get; private set; }
    public HourCountVM? PeakHour { get; private set; }
    public int DaysWithOrders { get; private set; }
    public decimal RevenuePerDay => Days == 0 ? 0 : Sales.TotalRevenue / Days;
    public double OrdersPerDay => Days == 0 ? 0 : (double)Sales.TotalOrders / Days;
    public decimal PopularRevenueTotal { get; private set; }
    public int PopularQuantityTotal { get; private set; }

    public static ReportData Build(DashboardVM overview, List<PopularItemVM> popular, List<CustomerActivityVM> customers,
        List<RecentReviewVM> reviews, DateTime generatedAt)
    {
        var d = new ReportData
        {
            Overview = overview,
            Start = overview.StartDate.Date,
            End = overview.EndDate.Date,
            GeneratedAt = generatedAt,
            Popular = popular,
            Customers = customers,
            Reviews = reviews
        };

        // Every day of the period, so a quiet day shows as a dip (and a zero row) rather than disappearing.
        var byDate = overview.Sales.DailyBreakdown.GroupBy(x => x.Date.Date).ToDictionary(g => g.Key, g => g.First());
        d.Daily = Enumerable.Range(0, d.Days).Select(i =>
        {
            var day = d.Start.AddDays(i);
            byDate.TryGetValue(day, out var hit);
            return new DailySalesPointVM { Date = day, OrderCount = hit?.OrderCount ?? 0, Revenue = hit?.Revenue ?? 0 };
        }).ToList();

        var byHour = overview.ReservationStats.ByHour;
        if (byHour.Count > 0)
        {
            var lo = byHour.Min(h => h.Hour);
            var hi = byHour.Max(h => h.Hour);
            d.Hours = Enumerable.Range(lo, hi - lo + 1)
                .Select(h => new HourCountVM { Hour = h, Count = byHour.Where(x => x.Hour == h).Sum(x => x.Count) }).ToList();
        }

        d.DaysWithOrders = d.Daily.Count(x => x.OrderCount > 0);
        if (d.DaysWithOrders > 0)
        {
            d.BestDay = d.Daily.OrderByDescending(x => x.Revenue).First();
            d.QuietestDay = d.Daily.Where(x => x.OrderCount > 0).OrderBy(x => x.Revenue).First();
        }
        d.PeakHour = d.Hours.Where(h => h.Count > 0).OrderByDescending(h => h.Count).FirstOrDefault();

        d.Weekdays = Enumerable.Range(1, 7).Select(i => (DayOfWeek)(i % 7)).Select(w => new WeekdayShare
        {
            Day = w,
            DaysInPeriod = d.Daily.Count(x => x.Date.DayOfWeek == w),
            Orders = d.Daily.Where(x => x.Date.DayOfWeek == w).Sum(x => x.OrderCount),
            Revenue = d.Daily.Where(x => x.Date.DayOfWeek == w).Sum(x => x.Revenue)
        }).ToList();

        d.PopularRevenueTotal = popular.Sum(p => p.RevenueGenerated);
        d.PopularQuantityTotal = popular.Sum(p => p.QuantitySold);
        d.Categories = popular
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "Other" : p.Category)
            .Select(g => new CategoryShare
            {
                Name = g.Key,
                Items = g.Count(),
                Quantity = g.Sum(p => p.QuantitySold),
                Revenue = g.Sum(p => p.RevenueGenerated)
            })
            .OrderByDescending(c => c.Revenue).ThenBy(c => c.Name).ToList();
        foreach (var c in d.Categories) c.Share = d.PopularRevenueTotal > 0 ? (double)(c.Revenue / d.PopularRevenueTotal) : 0;

        // The same five statuses, in the same order, as the Reservation status panel (Pending is left out there too).
        var r = overview.ReservationStats;
        var total = r.StatusTotal;
        d.Statuses = new[]
        {
            ("Completed", r.Completed), ("Confirmed", r.Confirmed), ("Seated", r.Seated), ("Cancelled", r.Cancelled), ("No-show", r.NoShow)
        }.Select(s => new StatusRow { Label = s.Item1, Count = s.Item2, Share = total == 0 ? 0 : (double)s.Item2 / total }).ToList();

        return d;
    }
}
