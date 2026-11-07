using System.Globalization;
using RROMS.Presentation;
using RROMS.ViewModels;

namespace RROMS.Services.Reporting;

/// <summary>
/// The Excel download: an Overview sheet that looks like the analytics page (figure cards and charts), followed by one
/// sheet of full detail for each part of the report, so nothing the page summarises is lost.
/// </summary>
internal static class ReportXlsx
{
    private const string Cocoa = "442D1C", Wine = "743014", Caramel = "84592B", Olive = "9D9167", Batter = "E8D1A7",
        Paper = "FBF6EA", Linen = "F3E8CE", CardBg = "FFFCF3", Ink2 = "6B5440", Sand = "C0A175", Rule = "EFE3C8";

    private static readonly string[] Slices = { Wine, Caramel, Olive, Sand, Cocoa, "8A7A52", Batter, "5C5636" };

    private static readonly string Rm0 = "\"" + Brand.Currency + " \"#,##0";
    private const string Money = "#,##0.00";
    private const string DateFmt = "d mmm yyyy";
    private const string DateTimeFmt = "d mmm yyyy hh:mm";
    private const string Percent = "0.0%";

    private sealed record Col(string Header, double Width, string? Fmt = null, string Align = "");

    // ------------------------------------------------------------------ styles

    private static XlsxStyle Head(string align) => new(Size: 10, Bold: true, Color: Paper, Fill: Cocoa, Halign: align, Wrap: true);
    private static XlsxStyle Body(string? fmt, string align, bool band, bool wrap = false) =>
        new(Size: 10, Color: Cocoa, Fill: band ? Paper : null, Halign: align, NumFmt: fmt, Border: "bottom", BorderColor: Rule, Wrap: wrap, Valign: wrap ? "top" : "center");
    private static XlsxStyle TotalStyle(string? fmt, string align) =>
        new(Size: 10, Bold: true, Color: Cocoa, Fill: Linen, Halign: align, NumFmt: fmt, Border: "top", BorderColor: Wine);
    private static readonly XlsxStyle TitleStyle = new(Font: "Georgia", Size: 18, Color: Cocoa);
    private static readonly XlsxStyle SubStyle = new(Size: 10, Color: Ink2, Italic: true);
    private static readonly XlsxStyle SectionStyle = new(Font: "Georgia", Size: 13, Color: Cocoa, Border: "bottom", BorderColor: Batter);
    private static readonly XlsxStyle NoteStyle = new(Size: 9, Color: Ink2, Wrap: true, Valign: "top");

    // ------------------------------------------------------------------ helpers

    /// <summary>Writes a table (header row, then rows). The last row is a totals row when asked. Returns the first data row.</summary>
    private static int Table(XlsxSheet sh, int headerRow, Col[] cols, IList<object?[]> rows, bool lastIsTotal = false, int firstColumn = 1)
    {
        for (var c = 0; c < cols.Length; c++)
        {
            sh.Set(headerRow, firstColumn + c, cols[c].Header, Head(cols[c].Align));
            sh.WidthAtLeast(firstColumn + c, cols[c].Width);
        }
        sh.Height(headerRow, 30);
        for (var r = 0; r < rows.Count; r++)
        {
            var total = lastIsTotal && r == rows.Count - 1;
            for (var c = 0; c < cols.Length; c++)
            {
                var style = total ? TotalStyle(cols[c].Fmt, cols[c].Align) : Body(cols[c].Fmt, cols[c].Align, r % 2 == 1, cols[c].Width >= 40);
                sh.Set(headerRow + 1 + r, firstColumn + c, c < rows[r].Length ? rows[r][c] : null, style);
            }
        }
        return headerRow + 1;
    }

    private static void TitleBlock(XlsxSheet sh, ReportData d, string title, string note)
    {
        sh.Set(1, 1, title, TitleStyle);
        sh.Height(1, 28);
        sh.Set(2, 1, Fmt.Period(d.Start, d.End) + ", " + note, SubStyle);
        sh.FreezeRows = 4;
    }

    private static XlsxSheet NewSheet(XlsxBook book, ReportData d, string name, string title, string note)
    {
        var sh = book.AddSheet(name);
        sh.TabColor = Wine;
        TitleBlock(sh, d, title, note);
        return sh;
    }

    private static decimal R2(decimal v) => Math.Round(v, 2);

    // ------------------------------------------------------------------ the workbook

    public static byte[] Build(ReportData d)
    {
        var book = new XlsxBook { Title = d.Restaurant + " - Overview report, " + Fmt.Period(d.Start, d.End), Author = d.Restaurant };

        var overview = book.AddSheet("Overview");
        overview.TabColor = Cocoa;

        var sales = SalesByDay(book, d, out var salesFirst, out var salesLast);
        var weekday = Weekdays(book, d, out var wdFirst);
        var popular = PopularItems(book, d, out var popFirst);
        var cats = Categories(book, d, out var catFirst);
        var res = Reservations(book, d, out var statusFirst, out var hourFirst);
        var fb = Feedback(book, d, out var distFirst, out var trendFirst);
        Reviews(book, d);
        Customers(book, d);
        Activity(book, d);

        Overview(overview, d, sales, salesFirst, salesLast, weekday, wdFirst, popular, popFirst, cats, catFirst, res, statusFirst, hourFirst, fb, distFirst, trendFirst);
        return book.Build();
    }

    // ------------------------------------------------------------------ detail sheets

    private static XlsxSheet SalesByDay(XlsxBook book, ReportData d, out int first, out int last)
    {
        var sh = NewSheet(book, d, "Sales by day", "Sales by day", "every day in the period, quiet days included. Cancelled orders are left out.");
        var cols = new[]
        {
            new Col("Date", 14, DateFmt, "left"), new Col("Day", 14, null, "left"), new Col("Orders", 10, "#,##0", "right"),
            new Col("Revenue (" + Brand.Currency + ")", 14, Money, "right"), new Col("Average order (" + Brand.Currency + ")", 16, Money, "right"),
            new Col("Running total (" + Brand.Currency + ")", 16, Money, "right"), new Col("Share of revenue", 12, Percent, "right")
        };
        var total = d.Sales.TotalRevenue;
        decimal running = 0;
        var rows = new List<object?[]>();
        foreach (var day in d.Daily)
        {
            running += day.Revenue;
            rows.Add(new object?[]
            {
                day.Date, Fmt.Day(day.Date), day.OrderCount, R2(day.Revenue), day.OrderCount == 0 ? null : R2(day.Revenue / day.OrderCount),
                R2(running), total == 0 ? 0m : day.Revenue / total
            });
        }
        first = 5;
        last = first + d.Daily.Count - 1;
        Table(sh, 4, cols, rows);
        var n = d.Daily.Count;
        sh.Set(last + 1, 1, "Total", TotalStyle(null, "left"));
        sh.Set(last + 1, 2, null, TotalStyle(null, "left"));
        sh.Set(last + 1, 3, new XlsxFormula($"SUM(C{first}:C{last})", d.Sales.TotalOrders), TotalStyle("#,##0", "right"));
        sh.Set(last + 1, 4, new XlsxFormula($"SUM(D{first}:D{last})", (double)R2(d.Sales.TotalRevenue)), TotalStyle(Money, "right"));
        sh.Set(last + 1, 5, R2(d.Sales.AverageOrderValue), TotalStyle(Money, "right"));
        sh.Set(last + 1, 6, null, TotalStyle(Money, "right"));
        sh.Set(last + 1, 7, null, TotalStyle(Percent, "right"));
        if (n > 0)
        {
            sh.AutoFilter(4, 1, last, 7);
            sh.DataBar(first, 4, last, 4, Caramel);
        }
        return sh;
    }

    private static XlsxSheet Weekdays(XlsxBook book, ReportData d, out int first)
    {
        var sh = NewSheet(book, d, "Weekdays", "Sales by weekday", "which weekdays earn the most. Averages count every such weekday in the period.");
        var cols = new[]
        {
            new Col("Weekday", 16, null, "left"), new Col("Days in period", 12, "#,##0", "right"), new Col("Orders", 10, "#,##0", "right"),
            new Col("Revenue (" + Brand.Currency + ")", 14, Money, "right"), new Col("Average revenue a day (" + Brand.Currency + ")", 20, Money, "right"),
            new Col("Average orders a day", 16, "0.0", "right"), new Col("Share of revenue", 12, Percent, "right")
        };
        var total = d.Sales.TotalRevenue;
        var rows = d.Weekdays.Select(w => new object?[]
        {
            w.Day.ToString(), w.DaysInPeriod, w.Orders, R2(w.Revenue), R2(w.AverageRevenue),
            w.DaysInPeriod == 0 ? 0.0 : (double)w.Orders / w.DaysInPeriod, total == 0 ? 0m : w.Revenue / total
        }).ToList();
        first = 5;
        Table(sh, 4, cols, rows);
        var last = first + rows.Count - 1;
        sh.Set(last + 1, 1, "Total", TotalStyle(null, "left"));
        sh.Set(last + 1, 2, new XlsxFormula($"SUM(B{first}:B{last})", d.Daily.Count), TotalStyle("#,##0", "right"));
        sh.Set(last + 1, 3, new XlsxFormula($"SUM(C{first}:C{last})", d.Weekdays.Sum(w => w.Orders)), TotalStyle("#,##0", "right"));
        sh.Set(last + 1, 4, new XlsxFormula($"SUM(D{first}:D{last})", (double)R2(d.Weekdays.Sum(w => w.Revenue))), TotalStyle(Money, "right"));
        sh.Set(last + 1, 5, null, TotalStyle(Money, "right"));
        sh.Set(last + 1, 6, null, TotalStyle("0.0", "right"));
        sh.Set(last + 1, 7, null, TotalStyle(Percent, "right"));
        sh.DataBar(first, 5, last, 5, Caramel);
        return sh;
    }

    private static XlsxSheet PopularItems(XlsxBook book, ReportData d, out int first)
    {
        var sh = NewSheet(book, d, "Popular items", "Popular items", "best sellers by number sold (up to " + Fmt.Int(d.Popular.Count) + " items). Cancelled orders are left out.");
        var cols = new[]
        {
            new Col("Rank", 7, "0", "right"), new Col("Item", 34, null, "left"), new Col("Category", 20, null, "left"),
            new Col("Quantity sold", 12, "#,##0", "right"), new Col("Revenue (" + Brand.Currency + ")", 14, Money, "right"),
            new Col("Share of quantity", 12, Percent, "right"), new Col("Share of revenue", 12, Percent, "right"), new Col("Average price (" + Brand.Currency + ")", 14, Money, "right")
        };
        var qty = d.PopularQuantityTotal;
        var rev = d.PopularRevenueTotal;
        var rows = d.Popular.Select((p, i) => new object?[]
        {
            i + 1, p.Name, p.Category, p.QuantitySold, R2(p.RevenueGenerated), qty == 0 ? 0.0 : (double)p.QuantitySold / qty,
            rev == 0 ? 0m : p.RevenueGenerated / rev, p.QuantitySold == 0 ? null : R2(p.RevenueGenerated / p.QuantitySold)
        }).ToList();
        first = 5;
        Table(sh, 4, cols, rows);
        var last = first + rows.Count - 1;
        if (rows.Count > 0)
        {
            sh.Set(last + 1, 1, null, TotalStyle(null, "right"));
            sh.Set(last + 1, 2, "Total", TotalStyle(null, "left"));
            sh.Set(last + 1, 3, null, TotalStyle(null, "left"));
            sh.Set(last + 1, 4, new XlsxFormula($"SUM(D{first}:D{last})", qty), TotalStyle("#,##0", "right"));
            sh.Set(last + 1, 5, new XlsxFormula($"SUM(E{first}:E{last})", (double)R2(rev)), TotalStyle(Money, "right"));
            sh.Set(last + 1, 6, null, TotalStyle(Percent, "right"));
            sh.Set(last + 1, 7, null, TotalStyle(Percent, "right"));
            sh.Set(last + 1, 8, null, TotalStyle(Money, "right"));
            sh.AutoFilter(4, 1, last, 8);
            sh.DataBar(first, 4, last, 4, Caramel);
            sh.DataBar(first, 5, last, 5, Wine);
        }
        return sh;
    }

    private static XlsxSheet Categories(XlsxBook book, ReportData d, out int first)
    {
        var sh = NewSheet(book, d, "Menu categories", "Sales by menu category", "worked out from the best-selling items on the Popular items sheet.");
        var cols = new[]
        {
            new Col("Category", 26, null, "left"), new Col("Items listed", 12, "#,##0", "right"), new Col("Quantity sold", 14, "#,##0", "right"),
            new Col("Revenue (" + Brand.Currency + ")", 14, Money, "right"), new Col("Share of revenue", 14, Percent, "right")
        };
        var rows = d.Categories.Select(c => new object?[] { c.Name, c.Items, c.Quantity, R2(c.Revenue), c.Share }).ToList();
        first = 5;
        Table(sh, 4, cols, rows);
        var last = first + rows.Count - 1;
        if (rows.Count > 0)
        {
            sh.Set(last + 1, 1, "Total", TotalStyle(null, "left"));
            sh.Set(last + 1, 2, new XlsxFormula($"SUM(B{first}:B{last})", d.Categories.Sum(c => c.Items)), TotalStyle("#,##0", "right"));
            sh.Set(last + 1, 3, new XlsxFormula($"SUM(C{first}:C{last})", d.PopularQuantityTotal), TotalStyle("#,##0", "right"));
            sh.Set(last + 1, 4, new XlsxFormula($"SUM(D{first}:D{last})", (double)R2(d.PopularRevenueTotal)), TotalStyle(Money, "right"));
            sh.Set(last + 1, 5, null, TotalStyle(Percent, "right"));
            sh.DataBar(first, 4, last, 4, Wine);
        }
        return sh;
    }

    private static XlsxSheet Reservations(XlsxBook book, ReportData d, out int statusFirst, out int hourFirst)
    {
        var sh = NewSheet(book, d, "Reservations", "Reservations", "status, party size and busiest hours. Pending reservations are not part of the status shares.");
        var r = d.Reservations;

        var statusCols = new[] { new Col("Status", 22, null, "left"), new Col("Reservations", 14, "#,##0", "right"), new Col("Share", 12, Percent, "right") };
        var statusRows = d.Statuses.Select(s => new object?[] { s.Label, s.Count, s.Share }).ToList();
        statusFirst = 5;
        Table(sh, 4, statusCols, statusRows);
        var sLast = statusFirst + statusRows.Count - 1;
        sh.Set(sLast + 1, 1, "Counted in total", TotalStyle(null, "left"));
        sh.Set(sLast + 1, 2, new XlsxFormula($"SUM(B{statusFirst}:B{sLast})", r.StatusTotal), TotalStyle("#,##0", "right"));
        sh.Set(sLast + 1, 3, null, TotalStyle(Percent, "right"));
        sh.Set(sLast + 2, 1, "Pending (not counted)", Body(null, "left", false));
        sh.Set(sLast + 2, 2, r.Pending, Body("#,##0", "right", false));
        sh.Set(sLast + 3, 1, "Average party size (guests)", Body(null, "left", true));
        sh.Set(sLast + 3, 2, r.AveragePartySize, Body("0.0", "right", true));
        sh.Set(sLast + 4, 1, "Reservations a day", Body(null, "left", false));
        sh.Set(sLast + 4, 2, d.Days == 0 ? 0.0 : (double)r.StatusTotal / d.Days, Body("0.0", "right", false));
        sh.DataBar(statusFirst, 2, sLast, 2, Caramel);

        var hourHeader = sLast + 7;
        sh.Set(hourHeader - 1, 1, "By hour", SectionStyle);
        var hourCols = new[] { new Col("Hour", 22, null, "left"), new Col("Reservations", 14, "#,##0", "right"), new Col("Share", 12, Percent, "right") };
        var allHours = r.ByHour.Sum(h => h.Count);
        var hourRows = d.Hours.Select(h => new object?[] { Fmt.Hour(h.Hour), h.Count, allHours == 0 ? 0.0 : (double)h.Count / allHours }).ToList();
        hourFirst = hourHeader + 1;
        if (hourRows.Count == 0) sh.Set(hourHeader, 1, "No reservations in this period.", SubStyle);
        else
        {
            Table(sh, hourHeader, hourCols, hourRows);
            sh.DataBar(hourFirst, 2, hourHeader + hourRows.Count, 2, Caramel);
        }
        return sh;
    }

    private static XlsxSheet Feedback(XlsxBook book, ReportData d, out int distFirst, out int trendFirst)
    {
        var sh = NewSheet(book, d, "Guest feedback", "Guest feedback", "what guests said in their reviews.");
        var f = d.Feedback;
        var row = 4;
        distFirst = 0;
        trendFirst = 0;

        sh.Set(row, 1, "Summary", SectionStyle); row++;
        var enough = f.TotalCount >= FeedbackSummaryVM.MinResponsesForAverage;
        var summary = new List<object?[]>
        {
            new object?[] { "Reviews in the period", f.TotalCount },
            new object?[] { "Average rating (out of 5)", enough ? f.AverageRating : null },
            new object?[] { "Net score", f.Nps.HasEnoughData ? f.Nps.Score : null },
            new object?[] { "Promoters", f.Nps.Promoters },
            new object?[] { "Passives", f.Nps.Passives },
            new object?[] { "Detractors", f.Nps.Detractors },
            new object?[] { "Answers to the net score question", f.Nps.Responses }
        };
        for (var i = 0; i < summary.Count; i++)
        {
            sh.Set(row, 1, summary[i][0], Body(null, "left", i % 2 == 1));
            sh.Set(row, 2, summary[i][1], Body(i == 1 ? "0.0" : "#,##0", "right", i % 2 == 1));
            row++;
        }
        sh.WidthAtLeast(1, 34);
        sh.WidthAtLeast(2, 16);
        if (!enough) sh.Set(row, 1, "An average needs at least " + FeedbackSummaryVM.MinResponsesForAverage + " reviews; a net score needs " + FeedbackSummaryVM.MinResponsesForNps + " answers.", SubStyle);
        row += 3;

        sh.Set(row - 1, 1, "Rating distribution", SectionStyle);
        var dmax = f.RatingCounts.Values.Sum();
        var distRows = Enumerable.Range(0, 5).Select(i => 5 - i).Select(star => new object?[]
        {
            star + (star == 1 ? " star" : " stars"), f.RatingCounts.TryGetValue(star, out var c) ? c : 0, dmax == 0 ? 0.0 : (double)(f.RatingCounts.TryGetValue(star, out var c2) ? c2 : 0) / dmax
        }).ToList();
        Table(sh, row, new[] { new Col("Stars", 34, null, "left"), new Col("Reviews", 16, "#,##0", "right"), new Col("Share", 12, Percent, "right") }, distRows);
        distFirst = row + 1;
        sh.DataBar(distFirst, 2, row + 5, 2, Caramel);
        row += 5 + 3;

        sh.Set(row - 1, 1, "Scores by part of the visit", SectionStyle);
        var metricRows = f.Metrics.Select(m => new object?[] { m.Label, m.HasEnoughData ? m.Average : null, m.Responses }).ToList();
        if (metricRows.Count == 0) { sh.Set(row, 1, "No scores yet.", SubStyle); row += 3; }
        else
        {
            Table(sh, row, new[] { new Col("Area", 34, null, "left"), new Col("Average (out of 5)", 16, "0.0", "right"), new Col("Answers", 12, "#,##0", "right") }, metricRows);
            row += metricRows.Count + 3;
        }

        sh.Set(row - 1, 1, "Problems mentioned", SectionStyle);
        if (f.TopIssues.Count == 0) { sh.Set(row, 1, "No problems were flagged.", SubStyle); row += 3; }
        else
        {
            Table(sh, row, new[] { new Col("Problem", 34, null, "left"), new Col("Mentions", 16, "#,##0", "right") }, f.TopIssues.Select(i => new object?[] { i.Tag, i.Count }).ToList());
            sh.DataBar(row + 1, 2, row + f.TopIssues.Count, 2, Wine);
            row += f.TopIssues.Count + 3;
        }

        sh.Set(row - 1, 1, "Average rating by " + f.TrendPeriod, SectionStyle);
        if (f.Trend.Count == 0) sh.Set(row, 1, "No reviews to chart.", SubStyle);
        else
        {
            Table(sh, row, new[] { new Col("Period starting", 34, null, "left"), new Col("Average rating", 16, "0.0", "right"), new Col("Reviews", 12, "#,##0", "right") },
                f.Trend.Select(t => new object?[] { t.Label, Math.Round(t.AverageRating, 2), t.Count }).ToList());
            trendFirst = row + 1;
        }
        return sh;
    }

    private static void Reviews(XlsxBook book, ReportData d)
    {
        var sh = NewSheet(book, d, "Reviews", "Recent reviews", "the latest " + d.Reviews.Count + " reviews, newest first.");
        var cols = new[]
        {
            new Col("Date", 14, DateFmt, "left"), new Col("Guest", 24, null, "left"), new Col("Rating (of 5)", 10, "0", "center"), new Col("About", 16, null, "left"),
            new Col("Comment", 70, null, "left"), new Col("Problems mentioned", 28, null, "left"), new Col("Scores", 40, null, "left")
        };
        var rows = d.Reviews.Select(r => new object?[]
        {
            r.CreatedAt.Date, string.IsNullOrWhiteSpace(r.CustomerName) ? "A guest" : r.CustomerName, r.Rating, r.Context, r.Comments ?? "",
            string.Join(", ", r.IssueTags), string.Join(", ", r.ScoreSummary)
        }).ToList();
        Table(sh, 4, cols, rows);
        if (rows.Count > 0) sh.AutoFilter(4, 1, 4 + rows.Count, 7);
        else sh.Set(5, 1, "No reviews in this period.", SubStyle);
    }

    private static void Customers(XlsxBook book, ReportData d)
    {
        var sh = NewSheet(book, d, "Customers", "Customers", "everyone with an order or reservation in the period, biggest spenders first.");
        var cols = new[]
        {
            new Col("Rank", 7, "0", "right"), new Col("Customer", 28, null, "left"), new Col("Email", 34, null, "left"), new Col("Reservations", 13, "#,##0", "right"),
            new Col("Orders", 10, "#,##0", "right"), new Col("Total spent (" + Brand.Currency + ")", 16, Money, "right"),
            new Col("Average per order (" + Brand.Currency + ")", 18, Money, "right"), new Col("Last visit", 14, DateFmt, "left")
        };
        var rows = d.Customers.Select((c, i) => new object?[]
        {
            i + 1, c.FullName, c.Email, c.ReservationCount, c.OrderCount, R2(c.TotalSpent), c.OrderCount == 0 ? null : R2(c.TotalSpent / c.OrderCount), c.LastVisit?.Date
        }).ToList();
        Table(sh, 4, cols, rows);
        if (rows.Count > 0)
        {
            sh.AutoFilter(4, 1, 4 + rows.Count, 8);
            sh.DataBar(5, 6, 4 + rows.Count, 6, Wine);
        }
        else sh.Set(5, 1, "No customer activity in this period.", SubStyle);
    }

    private static void Activity(XlsxBook book, ReportData d)
    {
        var sh = NewSheet(book, d, "Recent activity", "Recent activity", "the latest orders and reservations when this report was made.");
        var cols = new[] { new Col("When", 20, DateTimeFmt, "left"), new Col("Type", 14, null, "left"), new Col("Where", 20, null, "left"), new Col("Details", 60, null, "left") };
        var rows = d.Activity.Select(a => new object?[] { a.When, a.Kind, a.Title, a.Summary }).ToList();
        Table(sh, 4, cols, rows);
        if (rows.Count == 0) sh.Set(5, 1, "Nothing yet.", SubStyle);
    }

    // ------------------------------------------------------------------ overview

    private static string Change(decimal? pct, string none) => pct == null ? none : pct == 0 ? "Same as yesterday" : Fmt.Signed(pct.Value) + "% on yesterday";

    private static void Card(XlsxSheet sh, int row, int c1, int c2, string label, object? value, string fmt, string note, bool dark = false)
    {
        var labelStyle = new XlsxStyle(Size: 9, Bold: true, Color: dark ? Batter : Ink2, Fill: dark ? Cocoa : Linen, Halign: "center", Border: "box", BorderColor: dark ? Cocoa : Batter);
        var valueStyle = new XlsxStyle(Font: "Georgia", Size: 20, Color: dark ? Paper : Cocoa, Fill: dark ? Cocoa : CardBg, Halign: "center", NumFmt: fmt, Border: "box", BorderColor: dark ? Cocoa : Batter);
        var noteStyle = new XlsxStyle(Size: 8, Italic: true, Color: dark ? Batter : Ink2, Fill: dark ? Cocoa : CardBg, Halign: "center", Border: "box", BorderColor: dark ? Cocoa : Batter);
        for (var c = c1; c <= c2; c++)
        {
            sh.Set(row, c, c == c1 ? label : null, labelStyle);
            sh.Set(row + 1, c, c == c1 ? value : null, valueStyle);
            sh.Set(row + 2, c, c == c1 ? note : null, noteStyle);
        }
        if (c2 > c1) { sh.Merge(row, c1, row, c2); sh.Merge(row + 1, c1, row + 1, c2); sh.Merge(row + 2, c1, row + 2, c2); }
        sh.Height(row, 18);
        sh.Height(row + 1, 34);
        sh.Height(row + 2, 16);
    }

    private static void Section(XlsxSheet sh, int row, string text, string? right = null)
    {
        for (var c = 1; c <= 12; c++) sh.Set(row, c, c == 1 ? text : null, SectionStyle);
        if (right != null) sh.Set(row, 12, right, new XlsxStyle(Size: 9, Color: Ink2, Halign: "right", Border: "bottom", BorderColor: Batter));
        sh.Height(row, 24);
    }

    private static void Overview(XlsxSheet sh, ReportData d, XlsxSheet sales, int salesFirst, int salesLast, XlsxSheet weekday, int wdFirst,
        XlsxSheet popular, int popFirst, XlsxSheet cats, int catFirst, XlsxSheet res, int statusFirst, int hourFirst,
        XlsxSheet fb, int distFirst, int trendFirst)
    {
        sh.Gridlines = false;
        for (var c = 1; c <= 12; c++) sh.Width(c, 11);

        // Title band.
        var band1 = new XlsxStyle(Font: "Georgia", Size: 22, Color: Paper, Fill: Cocoa, Indent: 1);
        var band2 = new XlsxStyle(Size: 11, Color: Batter, Fill: Cocoa, Indent: 1);
        for (var c = 1; c <= 12; c++) { sh.Set(1, c, c == 1 ? d.Restaurant : null, band1); sh.Set(2, c, c == 1 ? "Overview report, " + Fmt.Period(d.Start, d.End) + " (" + d.Days + " " + Fmt.Plural(d.Days, "day", "days") + "), made " + Fmt.DateTimeText(d.GeneratedAt) : null, band2); }
        sh.Merge(1, 1, 1, 12); sh.Merge(2, 1, 2, 12);
        sh.Height(1, 42); sh.Height(2, 22);

        var o = d.Overview;
        Section(sh, 4, "Today", "as of " + d.GeneratedAt.ToString("HH:mm", CultureInfo.InvariantCulture));
        Card(sh, 5, 1, 3, "Sales today", o.TodayRevenue, Rm0, Change(o.RevenueChangePercent, "Nothing sold yesterday to compare"), dark: true);
        Card(sh, 5, 4, 5, "Orders", o.TodayOrdersCount, "#,##0", Change(o.OrdersChangePercent, ""));
        Card(sh, 5, 6, 7, "Reservations", o.TodayReservationsCount, "#,##0", Change(o.ReservationsChangePercent, ""));
        Card(sh, 5, 8, 9, "In the kitchen", o.OrdersInKitchenCount, "#,##0", "");
        Card(sh, 5, 10, 12, "Pending reservations", o.PendingReservationsCount, "#,##0", "");

        Section(sh, 9, Fmt.Period(d.Start, d.End), d.Days + " " + Fmt.Plural(d.Days, "day", "days"));
        var s = d.Sales; var r = d.Reservations; var f = d.Feedback;
        Card(sh, 10, 1, 3, "Total orders", s.TotalOrders, "#,##0", Fmt.Num1(d.OrdersPerDay) + " a day");
        Card(sh, 10, 4, 6, "Total revenue", Math.Round(s.TotalRevenue, 2), Rm0, Fmt.Rm(d.RevenuePerDay) + " a day");
        Card(sh, 10, 7, 9, "Average order value", Math.Round(s.AverageOrderValue, 2), Rm0, "Per order");
        Card(sh, 10, 10, 12, "Discounts given", Math.Round(s.TotalDiscount, 2), Rm0, s.TotalSubTotal > 0 ? Fmt.Pct((double)(s.TotalDiscount / s.TotalSubTotal)) + " of " + Fmt.Rm(s.TotalSubTotal) : "Nothing before discounts");
        var enough = f.TotalCount >= FeedbackSummaryVM.MinResponsesForAverage;
        Card(sh, 14, 1, 3, "Reservations", r.StatusTotal, "#,##0", r.Pending > 0 ? Fmt.Int(r.Pending) + " still pending, not counted" : Fmt.Num1(d.Days == 0 ? 0 : (double)r.StatusTotal / d.Days) + " a day");
        Card(sh, 14, 4, 6, "Average party size", r.AveragePartySize > 0 ? r.AveragePartySize : null, "0.0", "Guests per booking");
        Card(sh, 14, 7, 9, "Guest rating (out of 5)", enough ? f.AverageRating : null, "0.0", Fmt.Int(f.TotalCount) + " " + Fmt.Plural(f.TotalCount, "review", "reviews") + (enough ? "" : ", too few for an average"));
        Card(sh, 14, 10, 12, "Net score", f.Nps.HasEnoughData ? f.Nps.Score : null, "+0;-0;0",
            f.Nps.HasEnoughData ? f.Nps.Promoters + " promoters, " + f.Nps.Detractors + " detractors" : "Needs " + FeedbackSummaryVM.MinResponsesForNps + " answers, has " + f.Nps.Responses);

        Section(sh, 18, "Charts", "drawn from the sheets that follow");
        var row = 20;
        var half = (From: 1, Mid: 7, To: 13);

        XlsxChart NewChart(ChartKind kind, string title, int r1, int c1, int r2, int c2) =>
            new() { Kind = kind, Title = title, FromRow = r1, FromColumn = c1, ToRow = r2, ToColumn = c2 };
        XlsxSeries Series(XlsxSheet src, string name, int catCol, int valCol, int first, int last, string color) =>
            new() { Name = name, Sheet = src, CategoryColumn = catCol, ValueColumn = valCol, FirstRow = first, LastRow = last, Color = color };

        // Revenue per day, full width.
        var revenue = NewChart(d.Daily.Count > 45 ? ChartKind.Area : ChartKind.Line, "Revenue per day (" + Brand.Currency + ")", row, half.From, row + 18, half.To);
        revenue.Series.Add(Series(sales, "Revenue (" + Brand.Currency + ")", 2, 4, salesFirst, salesLast, Wine));
        sh.Charts.Add(revenue);
        row += 20;

        var pending = new List<Func<int, int, XlsxChart>>();
        var topItems = Math.Min(10, d.Popular.Count);
        if (topItems > 0) pending.Add((r1, c1) =>
        {
            var ch = NewChart(ChartKind.Bar, "Popular items (number sold)", r1, c1, r1 + 18, c1 + 6);
            ch.DataLabels = true;
            ch.Series.Add(Series(popular, "Quantity sold", 2, 4, popFirst, popFirst + topItems - 1, Wine));
            return ch;
        });
        var topCats = Math.Min(8, d.Categories.Count);
        if (topCats > 0 && d.PopularRevenueTotal > 0) pending.Add((r1, c1) =>
        {
            var ch = NewChart(ChartKind.Doughnut, "Sales by menu category", r1, c1, r1 + 18, c1 + 6);
            ch.Legend = true; ch.DataLabels = true;
            var ser = Series(cats, "Revenue (" + Brand.Currency + ")", 1, 4, catFirst, catFirst + topCats - 1, Wine);
            ser.PointColors = Slices.Take(topCats).ToArray();
            ch.Series.Add(ser);
            return ch;
        });
        if (d.Weekdays.Any(w => w.Revenue > 0)) pending.Add((r1, c1) =>
        {
            var ch = NewChart(ChartKind.Column, "Average revenue by weekday (" + Brand.Currency + ")", r1, c1, r1 + 18, c1 + 6);
            ch.DataLabels = true;
            ch.Series.Add(Series(weekday, "Average revenue a day (" + Brand.Currency + ")", 1, 5, wdFirst, wdFirst + 6, Caramel));
            return ch;
        });
        if (d.Hours.Count > 0) pending.Add((r1, c1) =>
        {
            var ch = NewChart(ChartKind.Column, "Reservations by hour", r1, c1, r1 + 18, c1 + 6);
            ch.DataLabels = true;
            ch.Series.Add(Series(res, "Reservations", 1, 2, hourFirst, hourFirst + d.Hours.Count - 1, Wine));
            return ch;
        });
        if (r.StatusTotal > 0) pending.Add((r1, c1) =>
        {
            var ch = NewChart(ChartKind.Bar, "Reservation status", r1, c1, r1 + 18, c1 + 6);
            ch.DataLabels = true;
            ch.Series.Add(Series(res, "Reservations", 1, 2, statusFirst, statusFirst + d.Statuses.Count - 1, Caramel));
            return ch;
        });
        if (f.TotalCount > 0 && distFirst > 0) pending.Add((r1, c1) =>
        {
            var ch = NewChart(ChartKind.Column, "Reviews by star rating", r1, c1, r1 + 18, c1 + 6);
            ch.DataLabels = true;
            ch.Series.Add(Series(fb, "Reviews", 1, 2, distFirst, distFirst + 4, Wine));
            return ch;
        });
        if (f.Trend.Count >= 2 && trendFirst > 0) pending.Add((r1, c1) =>
        {
            var ch = NewChart(ChartKind.Line, "Average rating by " + f.TrendPeriod, r1, c1, r1 + 18, c1 + 6);
            ch.AxisFormat = "0.0"; ch.AxisMin = 1; ch.AxisMax = 5;
            ch.Series.Add(Series(fb, "Average rating", 1, 2, trendFirst, trendFirst + f.Trend.Count - 1, Caramel));
            return ch;
        });

        for (var i = 0; i < pending.Count; i++)
        {
            var left = i % 2 == 0;
            sh.Charts.Add(pending[i](row, left ? half.From : half.Mid));
            if (!left || i == pending.Count - 1) row += 20;
        }
        if (pending.Count % 2 == 1) row += 0;

        row += 1;
        var notes = "Orders, revenue, popular items and customer totals leave out cancelled orders. Reservation shares leave out pending reservations. " +
                    "Sales by weekday is the average for each weekday that falls in the period. Menu categories are worked out from the best-selling items on the Popular items sheet. " +
                    "The sheets after this one hold every figure behind the charts, ready to sort and filter.";
        for (var c = 1; c <= 12; c++) sh.Set(row, c, c == 1 ? notes : null, NoteStyle);
        sh.Merge(row, 1, row, 12);
        sh.Height(row, 44);
    }
}
