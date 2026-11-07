using System.Globalization;
using RROMS.Presentation;
using RROMS.ViewModels;

namespace RROMS.Services.Reporting;

/// <summary>
/// The PDF download: the Overview page laid out on A4 pages in the site's colours, with the same panels (today, the
/// period figures, revenue per day, popular items, reservations, guest feedback, top customers, recent activity)
/// and a few extra charts, followed by a day-by-day table.
/// </summary>
internal sealed class ReportPdf
{
    // The site's palette.
    private static readonly Rgb Cocoa = Rgb.Hex("442D1C");
    private static readonly Rgb Wine = Rgb.Hex("743014");
    private static readonly Rgb Caramel = Rgb.Hex("84592B");
    private static readonly Rgb Olive = Rgb.Hex("9D9167");
    private static readonly Rgb Batter = Rgb.Hex("E8D1A7");
    private static readonly Rgb Paper = Rgb.Hex("FBF6EA");
    private static readonly Rgb Linen = Rgb.Hex("F3E8CE");
    private static readonly Rgb CardBg = Rgb.Hex("FFFCF3");
    private static readonly Rgb Ink2 = Rgb.Hex("6B5440");
    private static readonly Rgb OliveDeep = Rgb.Hex("5C5636");
    private static readonly Rgb Edge = CardBg.Mix(Batter, 0.6);
    private static readonly Rgb Grid = CardBg.Mix(Batter, 0.55);
    private static readonly Rgb Sand = Batter.Mix(Caramel, 0.4);

    private const int S = PdfFonts.Sans, SB = PdfFonts.SansBold, T = PdfFonts.Serif, TB = PdfFonts.SerifBold, TI = PdfFonts.SerifItalic;
    private const double L = 36, W = PdfDocument.A4Width - 72, Gap = 8, Bottom = 794, ContinuedTop = 58;

    private readonly ReportData d;
    private readonly PdfDocument doc = new();
    private PdfPage p = null!;
    private double y;

    private ReportPdf(ReportData data) { d = data; }

    public static byte[] Build(ReportData data) => new ReportPdf(data).Render();

    private byte[] Render()
    {
        FirstPage();
        Today();
        PeriodFigures();
        RevenueCard();
        Row(150, WeekdayCard, CategoryCard);
        Row(226, PopularCard, HoursCard);
        Row(172, StatusCard, HighlightsCard);
        FeedbackSection();
        ReviewsSection();
        CustomersSection();
        ActivitySection();
        DailyTable();
        Notes();
        Footers();
        return doc.Build(d.Restaurant + " - Overview report, " + Fmt.Period(d.Start, d.End), d.Restaurant);
    }

    // ------------------------------------------------------------------ pages

    private void Background(PdfPage page) => page.Rect(0, 0, page.Width, page.Height, Paper);

    private void FirstPage()
    {
        p = doc.AddPage();
        Background(p);
        p.Rect(0, 0, p.Width, 96, Cocoa);
        p.Rect(0, 96, p.Width, 3, Wine);

        p.Rect(L, 25, 46, 46, null, Batter, 1, 7); // the logo goes here
        p.Text(L + 62, 22, d.Restaurant, T, 25, Paper);
        p.Text(L + 62, 55, "Overview report", S, 10, Batter);

        var right = L + W;
        p.Text(right, 22, "Report period", S, 8, Batter, TextAlign.Right);
        p.Text(right, 35, Fmt.Period(d.Start, d.End), T, 17, Paper, TextAlign.Right);
        p.Text(right, 58, d.Days + " " + Fmt.Plural(d.Days, "day", "days") + ", made " + Fmt.DateTimeText(d.GeneratedAt), S, 8, Batter, TextAlign.Right);
        y = 116;
    }

    private void NewPage()
    {
        p = doc.AddPage();
        Background(p);
        p.Text(L, 24, d.Restaurant, T, 12, Cocoa);
        p.Text(L + W, 27, "Overview report, " + Fmt.Period(d.Start, d.End), S, 8, Ink2, TextAlign.Right);
        p.Line(L, 44, L + W, 44, Batter, 1);
        y = ContinuedTop;
    }

    private void Need(double h)
    {
        if (y + h > Bottom) NewPage();
    }

    private void Footers()
    {
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var pg = doc.Pages[i];
            pg.Line(L, 806, L + W, 806, Batter, 0.8);
            pg.Text(L, 813, d.Restaurant + "  ·  Overview report  ·  " + Fmt.Period(d.Start, d.End), S, 7.5, Ink2);
            pg.Text(L + W, 813, "Page " + (i + 1) + " of " + doc.Pages.Count, S, 7.5, Ink2, TextAlign.Right);
        }
    }

    // ------------------------------------------------------------------ building blocks

    private void Heading(string text, string? right = null)
    {
        p.Text(L, y, text, T, 19, Cocoa);
        if (!string.IsNullOrEmpty(right)) p.Text(L + W, y + 7, right, S, 8.5, Ink2, TextAlign.Right);
        y += 28;
    }

    private void Card(double x, double yy, double w, double h) => p.Rect(x, yy, w, h, CardBg, Edge, 0.8, 9);

    private void CardTitle(double x, double yy, string title, string? sub = null)
    {
        p.Text(x + 16, yy + 13, title, T, 14.5, Cocoa);
        if (!string.IsNullOrEmpty(sub)) p.Text(x + 16, yy + 32, sub, S, 7.8, Ink2);
    }

    private void Empty(double x, double yy, double w, double h, string text) =>
        p.Text(x + w / 2, yy + h / 2 - 6, PdfText.Fit(text, TI, 10.5, w - 32), TI, 10.5, Ink2, TextAlign.Center);

    /// <summary>Two panels side by side (about 56% and 44% of the width), moved to a new page together if they do not fit.</summary>
    private void Row(double h, Action<double, double, double, double> left, Action<double, double, double, double> right)
    {
        Need(h + 8);
        var lw = Math.Round((W - Gap) * 0.565);
        left(L, y, lw, h);
        right(L + lw + Gap, y, W - lw - Gap, h);
        y += h + 12;
    }

    private static (double Top, double Step, int Ticks) Axis(double max)
    {
        if (max <= 0) return (1, 1, 1);
        var raw = max / 4.0;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var n = raw / mag;
        var nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10;
        var step = nice * mag;
        var ticks = (int)Math.Ceiling(max / step - 1e-9);
        return (step * ticks, step, ticks);
    }

    private static string Tick(double v, double step) =>
        step < 1 ? v.ToString("0.0#", CultureInfo.InvariantCulture) : v.ToString("#,##0", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ today

    private static string Change(decimal? pct, string noBaseline) =>
        pct == null ? noBaseline : pct == 0 ? "Same as yesterday" : Fmt.Signed(pct.Value) + "% on yesterday";

    private void Today()
    {
        Need(130);
        Heading("Today", "as of " + d.GeneratedAt.ToString("HH:mm", CultureInfo.InvariantCulture));
        var o = d.Overview;
        const double h = 84, dw = 196;

        p.Rect(L, y, dw, h, Cocoa, null, 0, 9);
        p.Text(L + 16, y + 14, "Sales today", S, 8.5, Batter);
        p.Text(L + 16, y + 28, Fmt.Rm(o.TodayRevenue), T, 28, Paper);
        p.Text(L + 16, y + 66, Change(o.RevenueChangePercent, "Nothing sold yesterday to compare"), S, 7.8, Batter);

        var cw = (W - dw - 2 * Gap) / 2;
        var ch = (h - Gap) / 2;
        var cx = L + dw + Gap;
        Stat(cx, y, cw, ch, "Reservations", o.TodayReservationsCount.ToString(CultureInfo.InvariantCulture), Change(o.ReservationsChangePercent, ""));
        Stat(cx + cw + Gap, y, cw, ch, "Orders", o.TodayOrdersCount.ToString(CultureInfo.InvariantCulture), Change(o.OrdersChangePercent, ""));
        Stat(cx, y + ch + Gap, cw, ch, "In the kitchen", o.OrdersInKitchenCount.ToString(CultureInfo.InvariantCulture), "");
        Stat(cx + cw + Gap, y + ch + Gap, cw, ch, "Pending reservations", o.PendingReservationsCount.ToString(CultureInfo.InvariantCulture), "");
        y += h + 16;
    }

    private void Stat(double x, double yy, double w, double h, string label, string value, string note)
    {
        Card(x, yy, w, h);
        p.Text(x + 13, yy + (note.Length > 0 ? 8 : 13), label, S, 8.5, Ink2);
        if (note.Length > 0) p.Text(x + 13, yy + 21, PdfText.Fit(note, S, 7, w - 70), S, 7, Ink2);
        p.Text(x + w - 13, yy + 6, value, T, 21, Cocoa, TextAlign.Right);
    }

    // ------------------------------------------------------------------ period figures

    private void PeriodFigures()
    {
        Need(190);
        Heading(Fmt.Period(d.Start, d.End), d.Days + " " + Fmt.Plural(d.Days, "day", "days"));
        var s = d.Sales;
        var r = d.Reservations;
        var f = d.Feedback;
        const double h = 56;
        var w = (W - 3 * Gap) / 4;

        Kpi(0, w, h, "Total orders", Fmt.Int(s.TotalOrders), Fmt.Num1(d.OrdersPerDay) + " a day");
        Kpi(1, w, h, "Total revenue", Fmt.Rm(s.TotalRevenue), Fmt.Rm(d.RevenuePerDay) + " a day");
        Kpi(2, w, h, "Average order value", Fmt.Rm(s.AverageOrderValue), "Per order");
        Kpi(3, w, h, "Discounts given", Fmt.Rm(s.TotalDiscount),
            s.TotalSubTotal > 0 ? Fmt.Pct((double)(s.TotalDiscount / s.TotalSubTotal)) + " of " + Fmt.Rm(s.TotalSubTotal) : "Nothing before discounts");
        y += h + Gap;

        Kpi(0, w, h, "Reservations", Fmt.Int(r.StatusTotal), r.Pending > 0 ? Fmt.Int(r.Pending) + " still pending, not counted" : Fmt.Num1(d.Days == 0 ? 0 : (double)r.StatusTotal / d.Days) + " a day");
        Kpi(1, w, h, "Average party size", r.AveragePartySize > 0 ? Fmt.Num1(r.AveragePartySize) : "-", "Guests per booking");
        Kpi(2, w, h, "Guest rating", f.TotalCount >= FeedbackSummaryVM.MinResponsesForAverage ? Fmt.Num1(f.AverageRating) + " / 5" : "-",
            Fmt.Int(f.TotalCount) + " " + Fmt.Plural(f.TotalCount, "review", "reviews") + (f.TotalCount >= FeedbackSummaryVM.MinResponsesForAverage ? "" : ", too few"));
        Kpi(3, w, h, "Net score", f.Nps.HasEnoughData && f.Nps.Score != null ? (f.Nps.Score >= 0 ? "+" : "") + f.Nps.Score : "-",
            f.Nps.HasEnoughData ? f.Nps.Promoters + " promoters, " + f.Nps.Detractors + " detractors" : "Needs " + FeedbackSummaryVM.MinResponsesForNps + " answers, has " + f.Nps.Responses);
        y += h + 14;
    }

    private void Kpi(int index, double w, double h, string label, string value, string sub)
    {
        var x = L + index * (w + Gap);
        Card(x, y, w, h);
        p.Text(x + 13, y + 9, label, S, 8, Ink2);
        p.Text(x + 13, y + 20, PdfText.Fit(value, T, 21, w - 26), T, 21, Cocoa);
        p.Text(x + 13, y + 43, PdfText.Fit(sub, S, 7.5, w - 26), S, 7.5, Ink2);
    }

    // ------------------------------------------------------------------ revenue per day

    private void RevenueCard()
    {
        const double h = 216;
        Need(h + 8);
        Card(L, y, W, h);
        CardTitle(L, y, "Revenue per day", Brand.Currency + ", every day in the period.");
        if (d.BestDay != null)
            p.Text(L + W - 16, y + 16, "Highest day " + Fmt.Day(d.BestDay.Date) + ", " + Fmt.Rm(d.BestDay.Revenue), S, 8, Ink2, TextAlign.Right);

        var values = d.Daily.Select(x => (double)x.Revenue).ToList();
        var labels = d.Daily.Select(x => Fmt.Short(x.Date)).ToList();
        var (top, step, ticks) = Axis(values.Count == 0 ? 0 : values.Max());
        LineChart(L + 52, y + 58, L + W - 28, y + h - 74, values, labels, 0, top, step, ticks, Wine, true, v => Fmt.Rm((decimal)v));

        // The four numbers under the chart.
        var sy = y + h - 44;
        p.Line(L + 16, sy - 6, L + W - 16, sy - 6, Edge, 0.8);
        var cw = (W - 32) / 4;
        Under(0, cw, sy, "Highest day", d.BestDay == null ? "-" : Fmt.Short(d.BestDay.Date) + ", " + Fmt.Rm(d.BestDay.Revenue));
        Under(1, cw, sy, "Quietest day", d.QuietestDay == null ? "-" : Fmt.Short(d.QuietestDay.Date) + ", " + Fmt.Rm(d.QuietestDay.Revenue));
        Under(2, cw, sy, "Average a day", Fmt.Rm(d.RevenuePerDay));
        Under(3, cw, sy, "Days with orders", d.DaysWithOrders + " of " + d.Days);
        y += h + 12;
    }

    private void Under(int i, double cw, double sy, string label, string value)
    {
        var x = L + 16 + i * cw;
        p.Text(x, sy, label, S, 7.8, Ink2);
        p.Text(x, sy + 12, PdfText.Fit(value, T, 13, cw - 8), T, 13, Cocoa);
    }

    /// <summary>A filled line chart with grid lines, an axis on the left, date labels underneath and the highest point marked.</summary>
    private void LineChart(double px0, double py0, double px1, double py1, IList<double> v, IList<string> xl,
        double bottom, double top, double step, int ticks, Rgb color, bool markPeak, Func<double, string> peakText)
    {
        var n = v.Count;
        double Yv(double val) => py1 - (val - bottom) / (top - bottom) * (py1 - py0);

        for (var t = 0; t <= ticks; t++)
        {
            var val = bottom + step * t;
            var yy = Yv(val);
            p.Line(px0, yy, px1, yy, t == 0 ? Olive.Mix(CardBg, 0.4) : Grid, t == 0 ? 0.9 : 0.6);
            p.Text(px0 - 8, yy - 3.6, Tick(val, step), S, 7, Ink2, TextAlign.Right);
        }
        if (n == 0 || v.All(x => x <= bottom))
        {
            p.Text((px0 + px1) / 2, (py0 + py1) / 2 - 8, "Nothing to show for this period", TI, 10.5, Ink2, TextAlign.Center);
            return;
        }

        double X(int i) => n == 1 ? (px0 + px1) / 2 : px0 + (px1 - px0) * i / (n - 1);

        if (n == 1)
        {
            p.Rect(X(0) - 18, Yv(v[0]), 36, py1 - Yv(v[0]), color, null, 0, 3);
        }
        else
        {
            var pts = Enumerable.Range(0, n).Select(i => (X(i), Yv(v[i]))).ToList();
            var area = new List<(double X, double Y)>(pts) { (X(n - 1), py1), (X(0), py1) };
            p.Polygon(area, CardBg.Mix(color, 0.16));
            p.Polyline(pts, color, 1.6);
            if (n <= 40) foreach (var pt in pts) p.Circle(pt.Item1, pt.Item2, 1.9, color);
        }

        if (markPeak)
        {
            var pk = 0;
            for (var i = 1; i < n; i++) if (v[i] > v[pk]) pk = i;
            p.Circle(X(pk), Yv(v[pk]), 3.6, color, CardBg, 1.4);
            var label = peakText(v[pk]);
            var lw = PdfText.Measure(label, SB, 7.5);
            var lx = Math.Min(Math.Max(X(pk), px0 + lw / 2), px1 - lw / 2 + 6);
            p.Text(lx, Yv(v[pk]) - 16, label, SB, 7.5, Cocoa, TextAlign.Center);
        }

        // Date labels: as many as fit, always including the first and the last.
        var maxLabels = Math.Max(2, (int)((px1 - px0) / 52));
        var count = Math.Min(n, maxLabels);
        for (var k = 0; k < count; k++)
        {
            var i = count == 1 ? 0 : (int)Math.Round(k * (n - 1) / (double)(count - 1));
            p.Text(X(i), py1 + 7, xl[i], S, 7, Ink2, TextAlign.Center);
        }
    }

    /// <summary>Columns with the value above each one and a label below; the chosen column is drawn in the stronger colour.</summary>
    private void Columns(double px0, double py0, double px1, double py1, IList<double> v, IList<string> vt, IList<string> xl, int highlight, Rgb normal, Rgb strong)
    {
        var n = v.Count;
        var max = v.Count == 0 ? 0 : v.Max();
        p.Line(px0, py1, px1, py1, Olive.Mix(CardBg, 0.4), 0.9);
        if (n == 0 || max <= 0)
        {
            p.Text((px0 + px1) / 2, (py0 + py1) / 2 - 8, "Nothing to show for this period", TI, 10.5, Ink2, TextAlign.Center);
            return;
        }
        var slot = (px1 - px0) / n;
        var bw = Math.Min(slot * 0.6, 30);
        for (var i = 0; i < n; i++)
        {
            var cx = px0 + slot * (i + 0.5);
            var bh = v[i] / max * (py1 - py0 - 14);
            if (bh > 0.4) p.Rect(cx - bw / 2, py1 - bh, bw, bh, i == highlight ? strong : normal, null, 0, Math.Min(3, bw / 2));
            if (v[i] > 0) p.Text(cx, py1 - bh - 11, vt[i], i == highlight ? SB : S, 7, i == highlight ? Cocoa : Ink2, TextAlign.Center);
            p.Text(cx, py1 + 6, xl[i], S, 7, Ink2, TextAlign.Center);
        }
    }

    // ------------------------------------------------------------------ weekday and menu category

    private void WeekdayCard(double x, double yy, double w, double h)
    {
        Card(x, yy, w, h);
        CardTitle(x, yy, "Sales by weekday", "Average revenue on each weekday, " + Brand.Currency + ".");
        var days = d.Weekdays;
        var v = days.Select(k => (double)k.AverageRevenue).ToList();
        var best = v.Count == 0 || v.Max() <= 0 ? -1 : v.IndexOf(v.Max());
        var vt = v.Select(a => a.ToString("#,##0", CultureInfo.InvariantCulture)).ToList();
        var xl = days.Select(k => k.Day.ToString().Substring(0, 3)).ToList();
        Columns(x + 20, yy + 56, x + w - 20, yy + h - 28, v, vt, xl, best, Sand, Wine);
    }

    private void CategoryCard(double x, double yy, double w, double h)
    {
        Card(x, yy, w, h);
        CardTitle(x, yy, "Sales by menu category", "Share of item sales.");
        if (d.Categories.Count == 0 || d.PopularRevenueTotal <= 0)
        {
            Empty(x, yy + 24, w, h - 24, "No items sold in this period");
            return;
        }

        var shown = d.Categories.Take(5).Select(c => (c.Name, c.Share)).ToList();
        var rest = 1 - shown.Sum(c => c.Share);
        if (d.Categories.Count > 5 && rest > 0.0005) shown.Add(("Other", rest));
        var colors = new[] { Wine, Caramel, Olive, Sand, Cocoa, Linen.Mix(Olive, 0.5) };

        var cx = x + 16 + 44;
        var cy = yy + 58 + (h - 58) / 2 - 10;
        double start = 0;
        for (var i = 0; i < shown.Count; i++)
        {
            var sweep = shown[i].Share * 360;
            if (sweep <= 0.2) continue;
            p.Slice(cx, cy, 44, 26, start, start + sweep, colors[i % colors.Length], CardBg);
            start += sweep;
        }
        p.Text(cx, cy - 9, Fmt.Rm(d.PopularRevenueTotal), T, 8.5, Cocoa, TextAlign.Center);
        p.Text(cx, cy + 2, "item sales", S, 6.5, Ink2, TextAlign.Center);

        var lx = x + 16 + 98;
        var ly = yy + 56;
        for (var i = 0; i < shown.Count; i++)
        {
            p.Rect(lx, ly + 2.5, 7, 7, colors[i % colors.Length], null, 0, 2);
            p.Text(lx + 12, ly, PdfText.Fit(shown[i].Name, S, 8, w - 16 - 98 - 12 - 34), S, 8, Cocoa);
            p.Text(x + w - 16, ly, Fmt.Pct(shown[i].Share), SB, 8, Cocoa, TextAlign.Right);
            ly += 15;
        }
    }

    // ------------------------------------------------------------------ popular items and hours

    private void PopularCard(double x, double yy, double w, double h)
    {
        Card(x, yy, w, h);
        CardTitle(x, yy, "Popular items", "Best sellers by number sold.");
        var top = d.Popular.Take(7).ToList();
        if (top.Count == 0) { Empty(x, yy + 24, w, h - 24, "No items sold in this period"); return; }

        var max = Math.Max(1, top.Max(i => i.QuantitySold));
        var ry = yy + 54;
        const double rowH = 24;
        foreach (var it in top)
        {
            var tail = it.QuantitySold + " sold   " + Fmt.Rm(it.RevenueGenerated);
            var tw = PdfText.Measure(tail, S, 8);
            p.Text(x + w - 16, ry, tail, S, 8, Ink2, TextAlign.Right);
            var name = PdfText.Fit(it.Name, SB, 8.5, w - 32 - tw - 10);
            var nw = p.Text(x + 16, ry, name, SB, 8.5, Cocoa);
            var cat = PdfText.Fit(it.Category, S, 7, Math.Max(0, w - 32 - tw - 10 - nw - 6));
            if (cat.Length > 2 && nw + 6 < w - 32 - tw - 10) p.Text(x + 16 + nw + 6, ry + 1.2, cat, S, 7, Olive);
            var bw = w - 32;
            p.Rect(x + 16, ry + 13, bw, 5, Linen, null, 0, 2.5);
            p.Rect(x + 16, ry + 13, Math.Max(3, bw * it.QuantitySold / max), 5, Wine, null, 0, 2.5);
            ry += rowH;
        }
    }

    private void HoursCard(double x, double yy, double w, double h)
    {
        Card(x, yy, w, h);
        CardTitle(x, yy, "Reservations by hour", "Bookings starting in each hour.");
        if (d.Hours.Count == 0) { Empty(x, yy + 24, w, h - 24, "No reservations in this period"); return; }

        var v = d.Hours.Select(k => (double)k.Count).ToList();
        var slot = (w - 40) / d.Hours.Count;
        var xl = d.Hours.Select(k => slot >= 26 ? Fmt.Hour(k.Hour) : k.Hour.ToString(CultureInfo.InvariantCulture)).ToList();
        var vt = d.Hours.Select(k => k.Count.ToString(CultureInfo.InvariantCulture)).ToList();
        var peak = d.PeakHour == null ? -1 : d.Hours.FindIndex(k => k.Hour == d.PeakHour.Hour);
        Columns(x + 20, yy + 56, x + w - 20, yy + h - 30, v, vt, xl, peak, Sand, Wine);
    }

    // ------------------------------------------------------------------ reservations

    private static Rgb StatusColor(string label) => label switch
    {
        "Completed" => Olive,
        "Confirmed" => Caramel,
        "Seated" => Wine,
        "Cancelled" => Sand,
        _ => Cocoa
    };

    private void StatusCard(double x, double yy, double w, double h)
    {
        Card(x, yy, w, h);
        var r = d.Reservations;
        CardTitle(x, yy, "Reservation status", Fmt.Int(r.StatusTotal) + " " + Fmt.Plural(r.StatusTotal, "reservation", "reservations") + " in the period, not counting pending ones.");
        var ry = yy + 50;
        foreach (var s in d.Statuses)
        {
            p.Text(x + 16, ry + 1, s.Label, S, 8.8, Cocoa);
            var bx = x + 16 + 66;
            var bw = w - 32 - 66 - 64;
            p.Rect(bx, ry + 3, bw, 7, Linen, null, 0, 3.5);
            if (s.Count > 0) p.Rect(bx, ry + 3, Math.Max(4, bw * s.Share), 7, StatusColor(s.Label), null, 0, 3.5);
            p.Text(x + w - 16, ry + 1, Fmt.Pct(s.Share), S, 8.8, Ink2, TextAlign.Right);
            p.Text(x + w - 16 - 34, ry + 1, Fmt.Int(s.Count), SB, 8.8, Cocoa, TextAlign.Right);
            ry += 24;
        }
    }

    private void HighlightsCard(double x, double yy, double w, double h)
    {
        Card(x, yy, w, h);
        CardTitle(x, yy, "Booking highlights");
        var r = d.Reservations;
        var ry = yy + 40;
        Big(x, ry, w, "Average party size", r.AveragePartySize > 0 ? Fmt.Num1(r.AveragePartySize) + " guests" : "-");
        Big(x, ry + 44, w, "Busiest hour", d.PeakHour == null ? "-" : Fmt.Hour(d.PeakHour.Hour) + ", " + d.PeakHour.Count + " " + Fmt.Plural(d.PeakHour.Count, "booking", "bookings"));
        Big(x, ry + 88, w, "Bookings a day", d.Days == 0 ? "-" : Fmt.Num1((double)r.StatusTotal / d.Days));
    }

    private void Big(double x, double yy, double w, string label, string value)
    {
        p.Text(x + 16, yy, label, S, 8, Ink2);
        p.Text(x + 16, yy + 12, PdfText.Fit(value, T, 20, w - 32), T, 20, Cocoa);
    }

    // ------------------------------------------------------------------ guest feedback

    private void FeedbackSection()
    {
        var f = d.Feedback;
        var trend = f.TrendHasEnoughData;
        var h = f.TotalCount == 0 ? 90 : trend ? 304 : 222;
        Need(h + 8);
        Card(L, y, W, h);
        CardTitle(L, y, "Guest feedback", f.TotalCount + " " + Fmt.Plural(f.TotalCount, "review", "reviews") + " in the period");
        if (f.TotalCount == 0)
        {
            Empty(L, y + 20, W, h - 20, "No one has reviewed us in this period.");
            y += h + 12;
            return;
        }

        var colW = (W - 32 - 2 * 18) / 3;
        var c1 = L + 16;
        var c2 = c1 + colW + 18;
        var c3 = c2 + colW + 18;
        var top = y + 54;

        // Overall rating and how the stars split.
        p.Text(c1, top, "Overall rating", SB, 8.5, Cocoa);
        var enough = f.TotalCount >= FeedbackSummaryVM.MinResponsesForAverage;
        if (enough)
        {
            var bw = p.Text(c1, top + 14, Fmt.Num1(f.AverageRating), T, 32, Cocoa);
            p.Text(c1 + bw + 6, top + 36, "out of 5", S, 8, Ink2);
            for (var i = 0; i < 5; i++) p.Star(c1 + 7 + i * 16, top + 62, 6.6, i < (int)Math.Round(f.AverageRating) ? Caramel : Linen.Mix(Olive, 0.3));
        }
        else
        {
            p.Text(c1, top + 16, "Too few reviews for an average yet.", S, 8, Ink2);
        }
        var dist = f.RatingCounts;
        var dmax = Math.Max(1, dist.Values.Max());
        var dy = top + 80;
        for (var star = 5; star >= 1; star--)
        {
            var n = dist.TryGetValue(star, out var c) ? c : 0;
            p.Text(c1, dy, star.ToString(CultureInfo.InvariantCulture), SB, 8, Cocoa);
            p.Star(c1 + 14, dy + 4.6, 3.8, Caramel);
            var bx = c1 + 24;
            var bw2 = colW - 24 - 28;
            p.Rect(bx, dy + 1.5, bw2, 6, Linen, null, 0, 3);
            if (n > 0) p.Rect(bx, dy + 1.5, Math.Max(4, bw2 * n / dmax), 6, Wine, null, 0, 3);
            p.Text(c1 + colW, dy, n.ToString(CultureInfo.InvariantCulture), SB, 8, Cocoa, TextAlign.Right);
            dy += 15;
        }

        // How guests rated the separate parts of the visit.
        p.Text(c2, top, "How guests rated us", SB, 8.5, Cocoa);
        var my = top + 20;
        if (f.Metrics.Count == 0) p.Text(c2, my, "No scores yet.", S, 8, Ink2);
        foreach (var m in f.Metrics.Take(6))
        {
            p.Text(c2, my, m.Label, S, 8.5, Cocoa);
            p.Text(c2 + colW, my, m.HasEnoughData ? Fmt.Num1(m.Average) : "-", SB, 8.5, Cocoa, TextAlign.Right);
            p.Rect(c2, my + 13, colW, 5, Linen, null, 0, 2.5);
            if (m.HasEnoughData) p.Rect(c2, my + 13, Math.Max(3, colW * Math.Min(5, m.Average) / 5), 5, Caramel, null, 0, 2.5);
            my += 26;
        }

        // Net score and the problems mentioned most.
        p.Text(c3, top, "Net score", SB, 8.5, Cocoa);
        var ny = top + 16;
        if (f.Nps.HasEnoughData && f.Nps.Score != null)
        {
            p.Text(c3, ny, (f.Nps.Score >= 0 ? "+" : "") + f.Nps.Score, T, 28, Cocoa);
            var tot = Math.Max(1, f.Nps.Promoters + f.Nps.Passives + f.Nps.Detractors);
            double bx = c3;
            foreach (var (n, col) in new[] { (f.Nps.Promoters, Olive), (f.Nps.Passives, Batter), (f.Nps.Detractors, Wine) })
            {
                var segment = colW * n / tot;
                if (segment > 0) { p.Rect(bx, ny + 38, segment, 7, col, null, 0, 0); bx += segment; }
            }
            p.Text(c3, ny + 50, f.Nps.Promoters + " promoters, " + f.Nps.Passives + " passive, " + f.Nps.Detractors + " detractors", S, 7.2, Ink2);
            ny += 76;
        }
        else
        {
            p.Text(c3, ny, PdfText.Fit("Needs " + FeedbackSummaryVM.MinResponsesForNps + " answers, has " + f.Nps.Responses + ".", S, 8, colW), S, 8, Ink2);
            ny += 30;
        }
        p.Text(c3, ny, "Problems mentioned most", SB, 8.5, Cocoa);
        ny += 16;
        if (f.TopIssues.Count == 0) p.Text(c3, ny, "None flagged.", S, 8, Ink2);
        var imax = f.TopIssues.Count == 0 ? 1 : Math.Max(1, f.TopIssues.Max(i => i.Count));
        foreach (var iss in f.TopIssues.Take(4))
        {
            p.Text(c3, ny, PdfText.Fit(iss.Tag, S, 8, colW - 22), S, 8, Cocoa);
            p.Text(c3 + colW, ny, iss.Count.ToString(CultureInfo.InvariantCulture), SB, 8, Cocoa, TextAlign.Right);
            p.Rect(c3, ny + 11.5, colW, 3.5, Linen, null, 0, 1.75);
            p.Rect(c3, ny + 11.5, Math.Max(2.5, colW * iss.Count / imax), 3.5, Wine, null, 0, 1.75);
            ny += 20;
        }

        if (trend)
        {
            var ty = y + 222;
            p.Line(L + 16, ty - 6, L + W - 16, ty - 6, Edge, 0.8);
            p.Text(c1, ty, "Average rating by " + f.TrendPeriod, SB, 8.5, Cocoa);
            var tv = f.Trend.Select(t => t.AverageRating).ToList();
            var tl = f.Trend.Select(t => t.Label).ToList();
            LineChart(L + 46, ty + 20, L + W - 30, ty + 56, tv, tl, 1, 5, 1, 4, Caramel, false, v => Fmt.Num1(v));
        }
        y += h + 12;
    }

    private void ReviewsSection()
    {
        if (d.Reviews.Count == 0) return;
        var shown = d.Reviews.Take(6).ToList();
        Need(40 + 56);
        Heading("Recent reviews", shown.Count + " of " + d.Reviews.Count + " shown");
        foreach (var rv in shown)
        {
            var width = W - 32;
            var lines = string.IsNullOrWhiteSpace(rv.Comments) ? new List<string>() : PdfText.Wrap(rv.Comments!, S, 8.5, width, 3);
            var tags = rv.IssueTags.Count == 0 ? "" : "Mentioned: " + string.Join(", ", rv.IssueTags);
            var h = 34 + lines.Count * 11.5 + (tags.Length > 0 ? 12 : 0) + (lines.Count > 0 || tags.Length > 0 ? 4 : 0);
            Need(h + 6);
            Card(L, y, W, h);
            for (var i = 0; i < 5; i++) p.Star(L + 22 + i * 13, y + 17, 5, i < rv.Rating ? Caramel : Linen.Mix(Olive, 0.3));
            var nx = L + 22 + 5 * 13 + 4;
            var nw = p.Text(nx, y + 11.5, PdfText.Fit(string.IsNullOrWhiteSpace(rv.CustomerName) ? "A guest" : rv.CustomerName, SB, 9, 190), SB, 9, Cocoa);
            p.Text(L + W - 16, y + 12, Fmt.Date(rv.CreatedAt) + "  ·  " + rv.Context, S, 7.8, Ink2, TextAlign.Right);
            var ty = y + 31;
            foreach (var line in lines) { p.Text(L + 16, ty, line, S, 8.5, Cocoa); ty += 11.5; }
            if (tags.Length > 0) p.Text(L + 16, ty + 2, PdfText.Fit(tags, S, 7.8, width), S, 7.8, Wine);
            y += h + 6;
        }
        y += 8;
    }

    // ------------------------------------------------------------------ tables

    /// <summary>A table that carries on over the page break, repeating its header row.</summary>
    private void Table(string[] headers, double[] widths, TextAlign[] aligns, IList<string[]> rows, double[]? bars = null, int barColumn = -1, bool lastIsTotal = false)
    {
        const double rowH = 17, headH = 20;
        var total = widths.Sum();
        var scale = W / total;
        var w = widths.Select(x => x * scale).ToArray();

        void Header()
        {
            p.Rect(L, y, W, headH, Cocoa, null, 0, 5);
            var hx = L;
            for (var c = 0; c < headers.Length; c++)
            {
                var tx = aligns[c] == TextAlign.Right ? hx + w[c] - 8 : aligns[c] == TextAlign.Center ? hx + w[c] / 2 : hx + 8;
                p.Text(tx, y + 6, headers[c], SB, 7.8, Paper, aligns[c]);
                hx += w[c];
            }
            y += headH;
        }

        Need(headH + rowH * 2);
        Header();
        for (var r = 0; r < rows.Count; r++)
        {
            if (y + rowH > Bottom) { NewPage(); Header(); }
            var isTotal = lastIsTotal && r == rows.Count - 1;
            if (isTotal) p.Rect(L, y, W, rowH, Linen, null, 0, 0);
            else if (r % 2 == 0) p.Rect(L, y, W, rowH, CardBg, null, 0, 0);
            p.Line(L, y + rowH, L + W, y + rowH, Edge, 0.5);
            var cx = L;
            for (var c = 0; c < headers.Length; c++)
            {
                var text = rows[r][c];
                if (c == barColumn && bars != null && r < bars.Length && !isTotal)
                {
                    var bw = (w[c] - 16) * bars[r];
                    if (bw > 0.5) p.Rect(cx + 8, y + 5.5, bw, 6, Wine, null, 0, 3);
                }
                else
                {
                    var tx = aligns[c] == TextAlign.Right ? cx + w[c] - 8 : aligns[c] == TextAlign.Center ? cx + w[c] / 2 : cx + 8;
                    p.Text(tx, y + 4.8, PdfText.Fit(text, isTotal ? SB : S, 8, w[c] - 16), isTotal ? SB : S, 8, Cocoa, aligns[c]);
                }
                cx += w[c];
            }
            y += rowH;
        }
        y += 14;
    }

    private void CustomersSection()
    {
        Need(30 + 20 + 17 * 2 + 10);
        var all = d.Customers;
        Heading("Top customers", all.Count + " " + Fmt.Plural(all.Count, "customer", "customers") + " with orders or reservations");
        if (all.Count == 0)
        {
            Card(L, y, W, 60);
            Empty(L, y, W, 60, "No customer activity in this period.");
            y += 74;
            return;
        }
        var rows = all.Take(10).Select((c, i) => new[]
        {
            (i + 1).ToString(CultureInfo.InvariantCulture), c.FullName, c.Email, c.ReservationCount.ToString(CultureInfo.InvariantCulture),
            c.OrderCount.ToString(CultureInfo.InvariantCulture), Fmt.Rm(c.TotalSpent), c.LastVisit == null ? "-" : Fmt.Date(c.LastVisit.Value)
        }).ToList();
        Table(new[] { "#", "Customer", "Email", "Bookings", "Orders", "Spent", "Last visit" },
            new double[] { 30, 112, 150, 52, 44, 68, 70 },
            new[] { TextAlign.Left, TextAlign.Left, TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right }, rows);
        if (all.Count > 10)
        {
            p.Text(L, y - 8, "and " + (all.Count - 10) + " more in the Excel and CSV downloads", TI, 9, Ink2);
            y += 14;
        }
    }

    private void ActivitySection()
    {
        var items = d.Activity;
        if (items.Count == 0) return;
        Need(30 + 20 + 17 * 2 + 10);
        Heading("Recent activity", "latest orders and reservations");
        var rows = items.Select(a => new[] { Fmt.DateTimeText(a.When), a.Kind, a.Title, a.Summary }).ToList();
        Table(new[] { "When", "Type", "Where", "Details" }, new double[] { 96, 62, 86, 279 },
            new[] { TextAlign.Left, TextAlign.Left, TextAlign.Left, TextAlign.Left }, rows);
    }

    private void DailyTable()
    {
        if (d.Daily.Count == 0) return;
        Need(30 + 20 + 17 * 2 + 10);
        Heading("Sales by day", "every day in the period");
        decimal running = 0;
        var max = d.Daily.Max(x => (double)x.Revenue);
        var rows = new List<string[]>();
        foreach (var day in d.Daily)
        {
            running += day.Revenue;
            rows.Add(new[]
            {
                Fmt.Day(day.Date) + " " + day.Date.Year, Fmt.Int(day.OrderCount), Fmt.Rm2(day.Revenue),
                day.OrderCount == 0 ? "-" : Fmt.Rm2(day.Revenue / day.OrderCount), Fmt.Rm2(running), ""
            });
        }
        var bars = d.Daily.Select(x => max <= 0 ? 0 : (double)x.Revenue / max).ToList();
        rows.Add(new[] { "Total", Fmt.Int(d.Sales.TotalOrders), Fmt.Rm2(d.Sales.TotalRevenue), Fmt.Rm2(d.Sales.AverageOrderValue), "", "" });
        bars.Add(0);
        Table(new[] { "Date", "Orders", "Revenue", "Average order", "Running total", "Compared with best day" },
            new double[] { 100, 52, 76, 80, 84, 131 },
            new[] { TextAlign.Left, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Right, TextAlign.Left },
            rows, bars.ToArray(), 5, true);
    }

    private void Notes()
    {
        Need(46);
        p.Text(L, y, "Notes", SB, 8.5, Cocoa);
        var notes = "Orders, revenue, popular items and customer totals leave out cancelled orders. Reservation shares leave out pending reservations. " +
                    "Sales by weekday is the average for each weekday that falls in the period. Sales by menu category covers the best-selling items listed in the Excel download.";
        var yy = y + 13;
        foreach (var line in PdfText.Wrap(notes, S, 7.8, W, 4)) { p.Text(L, yy, line, S, 7.8, Ink2); yy += 10.5; }
        y = yy + 4;
    }
}
