using System.Globalization;
using System.Text;
using RROMS.Presentation;
using RROMS.ViewModels;

namespace RROMS.Services.Reporting;

/// <summary>
/// The CSV download: one file holding every part of the report as its own block (a title line, a header line, the rows,
/// then a blank line), so it opens in Excel, Numbers or any text editor and nothing is lost for lack of charts.
/// </summary>
internal static class ReportCsv
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] Build(ReportData d)
    {
        var sb = new StringBuilder();

        void Line(params object?[] cells) => sb.Append(string.Join(",", cells.Select(Cell))).Append("\r\n");
        void Gap() => sb.Append("\r\n");
        void Block(string title, string[] headers, IEnumerable<object?[]> rows)
        {
            Line(title);
            Line(headers.Cast<object?>().ToArray());
            foreach (var r in rows) Line(r);
            Gap();
        }

        var o = d.Overview;
        var s = d.Sales;
        var r = d.Reservations;
        var f = d.Feedback;
        var enough = f.TotalCount >= FeedbackSummaryVM.MinResponsesForAverage;

        Block("Report", new[] { "Item", "Value" }, new[]
        {
            new object?[] { "Restaurant", d.Restaurant },
            new object?[] { "Report", "Overview report" },
            new object?[] { "Period start", d.Start },
            new object?[] { "Period end", d.End },
            new object?[] { "Days in period", d.Days },
            new object?[] { "Made on", d.GeneratedAt },
            new object?[] { "Currency", Brand.Currency },
            new object?[] { "Note", "Orders, revenue, popular items and customer totals leave out cancelled orders. Reservation shares leave out pending reservations." }
        });

        Block("Today (at the time of the report)", new[] { "Measure", "Value", "Change on yesterday (%)" }, new[]
        {
            new object?[] { "Sales today", Money(o.TodayRevenue), o.RevenueChangePercent },
            new object?[] { "Orders today", o.TodayOrdersCount, o.OrdersChangePercent },
            new object?[] { "Reservations today", o.TodayReservationsCount, o.ReservationsChangePercent },
            new object?[] { "Orders in the kitchen", o.OrdersInKitchenCount, null },
            new object?[] { "Pending reservations", o.PendingReservationsCount, null }
        });

        var peak = d.PeakHour;
        Block("Period summary", new[] { "Measure", "Value" }, new[]
        {
            new object?[] { "Total orders", s.TotalOrders },
            new object?[] { "Orders a day", Math.Round(d.OrdersPerDay, 2) },
            new object?[] { "Total revenue", Money(s.TotalRevenue) },
            new object?[] { "Revenue a day", Money(d.RevenuePerDay) },
            new object?[] { "Average order value", Money(s.AverageOrderValue) },
            new object?[] { "Sub-total before discounts", Money(s.TotalSubTotal) },
            new object?[] { "Discounts given", Money(s.TotalDiscount) },
            new object?[] { "Tax", Money(s.TotalTax) },
            new object?[] { "Days with orders", d.DaysWithOrders },
            new object?[] { "Highest day", d.BestDay?.Date },
            new object?[] { "Highest day revenue", d.BestDay == null ? null : Money(d.BestDay.Revenue) },
            new object?[] { "Quietest day with orders", d.QuietestDay?.Date },
            new object?[] { "Quietest day revenue", d.QuietestDay == null ? null : Money(d.QuietestDay.Revenue) },
            new object?[] { "Reservations counted (not pending)", r.StatusTotal },
            new object?[] { "Pending reservations in period", r.Pending },
            new object?[] { "Reservations a day", Math.Round(d.Days == 0 ? 0 : (double)r.StatusTotal / d.Days, 2) },
            new object?[] { "Average party size", r.AveragePartySize },
            new object?[] { "Busiest reservation hour", peak == null ? null : Fmt.Hour(peak.Hour) },
            new object?[] { "Reservations in busiest hour", peak?.Count },
            new object?[] { "Reviews", f.TotalCount },
            new object?[] { "Average rating (out of 5)", enough ? Math.Round(f.AverageRating, 2) : null },
            new object?[] { "Net score", f.Nps.HasEnoughData ? f.Nps.Score : null },
            new object?[] { "Promoters", f.Nps.Promoters },
            new object?[] { "Passives", f.Nps.Passives },
            new object?[] { "Detractors", f.Nps.Detractors }
        });

        decimal running = 0;
        var total = s.TotalRevenue;
        Block("Sales by day", new[] { "Date", "Day", "Orders", "Revenue", "Average order", "Running total", "Share of revenue (%)" },
            d.Daily.Select(x =>
            {
                running += x.Revenue;
                return new object?[] { x.Date, x.Date.DayOfWeek.ToString(), x.OrderCount, Money(x.Revenue), x.OrderCount == 0 ? null : Money(x.Revenue / x.OrderCount), Money(running), total == 0 ? 0.0 : Math.Round((double)(x.Revenue / total) * 100, 1) };
            }).ToList());

        Block("Sales by weekday", new[] { "Weekday", "Days in period", "Orders", "Revenue", "Average revenue a day", "Share of revenue (%)" },
            d.Weekdays.Select(w => new object?[] { w.Day.ToString(), w.DaysInPeriod, w.Orders, Money(w.Revenue), Money(w.AverageRevenue), total == 0 ? 0.0 : Math.Round((double)(w.Revenue / total) * 100, 1) }).ToList());

        var qty = d.PopularQuantityTotal;
        var rev = d.PopularRevenueTotal;
        Block("Popular items", new[] { "Rank", "Item", "Category", "Quantity sold", "Revenue", "Share of quantity (%)", "Share of revenue (%)", "Average price" },
            d.Popular.Select((p, i) => new object?[]
            {
                i + 1, p.Name, p.Category, p.QuantitySold, Money(p.RevenueGenerated), qty == 0 ? 0.0 : Math.Round((double)p.QuantitySold / qty * 100, 1),
                rev == 0 ? 0.0 : Math.Round((double)(p.RevenueGenerated / rev) * 100, 1), p.QuantitySold == 0 ? null : Money(p.RevenueGenerated / p.QuantitySold)
            }).ToList());

        Block("Sales by menu category", new[] { "Category", "Items listed", "Quantity sold", "Revenue", "Share of revenue (%)" },
            d.Categories.Select(c => new object?[] { c.Name, c.Items, c.Quantity, Money(c.Revenue), Math.Round(c.Share * 100, 1) }).ToList());

        Block("Reservation status", new[] { "Status", "Reservations", "Share (%)" },
            d.Statuses.Select(x => new object?[] { x.Label, x.Count, Math.Round(x.Share * 100, 1) })
                .Append(new object?[] { "Pending (not counted)", r.Pending, null }).ToList());

        var allHours = r.ByHour.Sum(h => h.Count);
        Block("Reservations by hour", new[] { "Hour", "Reservations", "Share (%)" },
            d.Hours.Select(h => new object?[] { Fmt.Hour(h.Hour), h.Count, allHours == 0 ? 0.0 : Math.Round((double)h.Count / allHours * 100, 1) }).ToList());

        var dist = f.RatingCounts.Values.Sum();
        Block("Guest feedback: rating distribution", new[] { "Stars", "Reviews", "Share (%)" },
            Enumerable.Range(0, 5).Select(i => 5 - i).Select(star =>
            {
                var n = f.RatingCounts.TryGetValue(star, out var c) ? c : 0;
                return new object?[] { star, n, dist == 0 ? 0.0 : Math.Round((double)n / dist * 100, 1) };
            }).ToList());
        Block("Guest feedback: scores by part of the visit", new[] { "Area", "Average (out of 5)", "Answers" },
            f.Metrics.Select(m => new object?[] { m.Label, m.HasEnoughData ? Math.Round(m.Average, 2) : null, m.Responses }).ToList());
        Block("Guest feedback: problems mentioned", new[] { "Problem", "Mentions" }, f.TopIssues.Select(i => new object?[] { i.Tag, i.Count }).ToList());
        Block("Guest feedback: average rating by " + f.TrendPeriod, new[] { "Period starting", "Average rating", "Reviews" },
            f.Trend.Select(t => new object?[] { t.Label, Math.Round(t.AverageRating, 2), t.Count }).ToList());

        Block("Recent reviews", new[] { "Date", "Guest", "Rating", "About", "Comment", "Problems mentioned", "Scores" },
            d.Reviews.Select(x => new object?[]
            {
                x.CreatedAt.Date, string.IsNullOrWhiteSpace(x.CustomerName) ? "A guest" : x.CustomerName, x.Rating, x.Context, x.Comments, string.Join("; ", x.IssueTags), string.Join("; ", x.ScoreSummary)
            }).ToList());

        Block("Customers", new[] { "Rank", "Customer", "Email", "Reservations", "Orders", "Total spent", "Average per order", "Last visit" },
            d.Customers.Select((c, i) => new object?[]
            {
                i + 1, c.FullName, c.Email, c.ReservationCount, c.OrderCount, Money(c.TotalSpent), c.OrderCount == 0 ? null : Money(c.TotalSpent / c.OrderCount), c.LastVisit?.Date
            }).ToList());

        Block("Recent activity", new[] { "When", "Type", "Where", "Details" }, d.Activity.Select(a => new object?[] { a.When, a.Kind, a.Title, a.Summary }).ToList());

        // A byte-order mark makes Excel read names such as "Mélancolie" correctly.
        var body = new UTF8Encoding(false).GetBytes(sb.ToString());
        return new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();
    }

    private static decimal Money(decimal v) => Math.Round(v, 2);

    private static string Cell(object? v)
    {
        switch (v)
        {
            case null: return "";
            case string s: return Quote(Guard(s));
            case DateTime dt: return dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", Inv) : dt.ToString("yyyy-MM-dd HH:mm", Inv);
            case decimal m: return m.ToString("0.00", Inv);
            case double dbl: return dbl.ToString("0.##", Inv);
            case bool b: return b ? "yes" : "no";
            default: return Convert.ToString(v, Inv) ?? "";
        }
    }

    /// <summary>
    /// Text that starts with = + - or @ would run as a formula when the file is opened in a spreadsheet. Guest names and review
    /// comments are typed by guests, so a leading apostrophe makes them plain text.
    /// </summary>
    private static string Guard(string s) => s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0 ? "'" + s : s;

    private static string Quote(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s.StartsWith(' ') || s.EndsWith(' ') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
