using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Admin;

/// 
/// The administrator's Overview: what is happening today (always today, whatever period is chosen below), then a period report
/// (sales, popular items, reservations, tables, guest feedback, best customers) with the same downloads the old Reports page had.
/// 
public class DashboardModel : PageModel
{
    private const int MaxDays = 366;

    private readonly IReportService _reportService;
    private readonly ApplicationDbContext _context;

    public DashboardModel(IReportService reportService, ApplicationDbContext context)
    {
        _reportService = reportService;
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime StartDate { get; set; } = DateTime.Today.AddDays(-29);

    [BindProperty(SupportsGet = true)]
    public DateTime EndDate { get; set; } = DateTime.Today;

    /// "Show only 3 stars and below" in the recent reviews.
    [BindProperty(SupportsGet = true)]
    public bool LowOnly { get; set; }

    public DashboardVM Data { get; set; } = new();
    public List<RecentReviewVM> RecentReviews { get; set; } = new();

    /// The eight days ending today, for the small line on the sales card (days without orders count as zero).
    public List<DailySalesPointVM> LastEight { get; private set; } = new();
    /// Every day of the chosen period, so a quiet day shows as a dip rather than disappearing.
    public List<DailySalesPointVM> Daily { get; private set; } = new();
    public List<HourCountVM> Hours { get; private set; } = new();
    public List<FloorMap.Zone> Floor { get; private set; } = new();
    public int TablesBooked { get; private set; }
    public int TablesSeated { get; private set; }
    public int TablesFree { get; private set; }
    public string? NextArrival { get; private set; }
    public string QuickRange { get; private set; } = "";

    /// Show the page, or, when a download button was pressed (format is set), send that report as a file instead.
    public async Task<IActionResult> OnGetAsync(string? report, string? format)
    {
        var today = DateTime.Today;
        StartDate = StartDate.Date; EndDate = EndDate.Date;
        if (EndDate < StartDate) (StartDate, EndDate) = (EndDate, StartDate);
        if (EndDate > today) EndDate = today;
        if (StartDate > EndDate) StartDate = EndDate;
        if ((EndDate - StartDate).TotalDays >= MaxDays) StartDate = EndDate.AddDays(-(MaxDays - 1));

        if (!string.IsNullOrEmpty(format)) return await ExportAsync(report ?? "sales", format);

        Data = await _reportService.GetDashboardDataAsync(StartDate, EndDate);
        RecentReviews = await _reportService.GetRecentReviewsAsync(StartDate, EndDate, LowOnly ? 3 : null, 6);

        var week = await _reportService.GetSalesReportAsync(today.AddDays(-7), today);
        LastEight = Enumerable.Range(-7, 8).Select(i =>
        {
            var d = today.AddDays(i);
            var hit = week.DailyBreakdown.FirstOrDefault(x => x.Date == d);
            return new DailySalesPointVM { Date = d, OrderCount = hit?.OrderCount ?? 0, Revenue = hit?.Revenue ?? 0 };
        }).ToList();

        var days = (int)(EndDate - StartDate).TotalDays + 1;
        Daily = Enumerable.Range(0, days).Select(i =>
        {
            var d = StartDate.AddDays(i);
            var hit = Data.Sales.DailyBreakdown.FirstOrDefault(x => x.Date == d);
            return new DailySalesPointVM { Date = d, OrderCount = hit?.OrderCount ?? 0, Revenue = hit?.Revenue ?? 0 };
        }).ToList();

        var byHour = Data.ReservationStats.ByHour;
        if (byHour.Count > 0)
            Hours = Enumerable.Range(byHour.Min(h => h.Hour), byHour.Max(h => h.Hour) - byHour.Min(h => h.Hour) + 1)
                .Select(h => new HourCountVM { Hour = h, Count = byHour.FirstOrDefault(x => x.Hour == h)?.Count ?? 0 }).ToList();

        QuickRange = EndDate != today ? "" :
            StartDate == today ? "today" :
            StartDate == today.AddDays(-6) ? "7" :
            StartDate == today.AddDays(-29) ? "30" :
            StartDate == new DateTime(today.Year, today.Month, 1) ? "month" : "";

        await LoadTonightAsync();
        return Page();
    }

    /// Which tables are seated, booked for later today, or free, and who arrives next.
    private async Task LoadTonightAsync()
    {
        var now = DateTime.Now;
        var from = DateTime.Today;
        var todays = await _context.Reservations.AsNoTracking()
            .Include(r => r.Customer).Include(r => r.Table)
            .Where(r => r.ReservationDateTime >= from && r.ReservationDateTime < from.AddDays(1)
                        && r.Status != ReservationStatus.Cancelled && r.Status != ReservationStatus.NoShow && r.Status != ReservationStatus.Completed)
            .OrderBy(r => r.ReservationDateTime).ToListAsync();
        var tables = await _context.RestaurantTables.AsNoTracking().Include(t => t.DiningArea).Where(t => t.IsActive).ToListAsync();

        var seatedBy = todays.Where(r => r.Status == ReservationStatus.Seated && r.TableId.HasValue)
            .GroupBy(r => r.TableId!.Value).ToDictionary(g => g.Key, g => g.First());
        var upcoming = todays.Where(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed && r.TableId.HasValue && r.ReservationEndDateTime > now)
            .GroupBy(r => r.TableId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(x => x.ReservationDateTime).First());

        Floor = tables.GroupBy(t => t.DiningArea?.Name ?? "Tables").OrderBy(g => g.Key)
            .Select(g => new FloorMap.Zone(g.Key, g
                .OrderBy(t => t.PositionY ?? int.MaxValue).ThenBy(t => t.PositionX ?? int.MaxValue).ThenBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber)
                .Select(t =>
                {
                    if (seatedBy.ContainsKey(t.Id)) return new FloorMap.Seat(t.TableNumber, t.Capacity, "seated", "Seated", $"{t.TableNumber}, seats {t.Capacity}: seated");
                    if (upcoming.TryGetValue(t.Id, out var next))
                        return new FloorMap.Seat(t.TableNumber, t.Capacity, "booked", next.ReservationDateTime.ToString("HH:mm"), $"{t.TableNumber}, seats {t.Capacity}: booked for {next.ReservationDateTime:HH:mm}");
                    return new FloorMap.Seat(t.TableNumber, t.Capacity, "free", "Free", $"{t.TableNumber}, seats {t.Capacity}: free");
                }).ToList())).ToList();
        TablesSeated = Floor.Sum(z => z.Seats.Count(s => s.State == "seated"));
        TablesBooked = Floor.Sum(z => z.Seats.Count(s => s.State == "booked"));
        TablesFree = Floor.Sum(z => z.Seats.Count(s => s.State == "free"));

        var arriving = todays.FirstOrDefault(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed && r.ReservationDateTime >= now.AddMinutes(-30));
        if (arriving != null)
            NextArrival = $"{arriving.ReservationDateTime:HH:mm}, {(arriving.Table != null ? arriving.Table.TableNumber + ", " : "")}{arriving.PartySize} {(arriving.PartySize == 1 ? "guest" : "guests")}";
    }

    /// 
    /// One place serves every download button on the page (they are part of the period form, so a download always
    /// matches the period on screen): /Admin/Dashboard?report=sales&amp;format=csv&amp;StartDate=...&amp;EndDate=...
    /// report: sales | popular | reservations | customers    format: csv | pdf | excel
    /// 
    private async Task<IActionResult> ExportAsync(string report, string format)
    {
        List<string> headers;
        List<List<string>> rows;
        string title;

        switch (report)
        {
            case "popular":
                title = "Popular Menu Items";
                headers = new() { "Item", "Category", "Quantity sold", "Revenue" };
                var popular = await _reportService.GetPopularItemsAsync(StartDate, EndDate, 100);
                rows = popular.Select(p => new List<string> { p.Name, p.Category, p.QuantitySold.ToString(), p.RevenueGenerated.ToString("F2") }).ToList();
                break;

            case "reservations":
                title = "Reservation Statistics";
                headers = new() { "Hour", "Reservation count" };
                var stats = await _reportService.GetReservationStatsAsync(StartDate, EndDate);
                rows = stats.ByHour.Select(h => new List<string> { $"{h.Hour}:00", h.Count.ToString() }).ToList();
                break;

            case "customers":
                title = "Customer Activity";
                headers = new() { "Customer", "Email", "Reservations", "Orders", "Total spent", "Last visit" };
                var activity = await _reportService.GetCustomerActivityAsync(StartDate, EndDate);
                rows = activity.Select(c => new List<string>
                {
                    c.FullName, c.Email, c.ReservationCount.ToString(), c.OrderCount.ToString(),
                    c.TotalSpent.ToString("F2"), c.LastVisit?.ToString("yyyy-MM-dd") ?? ""
                }).ToList();
                break;

            default: // sales
                title = "Sales Report";
                headers = new() { "Date", "Orders", "Revenue" };
                var sales = await _reportService.GetSalesReportAsync(StartDate, EndDate);
                rows = sales.DailyBreakdown.Select(d => new List<string> { d.Date.ToString("yyyy-MM-dd"), d.OrderCount.ToString(), d.Revenue.ToString("F2") }).ToList();
                break;
        }

        var fileNameBase = $"{report}-report-{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}";

        return format switch
        {
            "pdf" => File(ExportHelper.GeneratePdf(title, headers, rows), "application/pdf", fileNameBase + ".pdf"),
            "excel" => File(ExportHelper.GenerateExcel(title, headers, rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileNameBase + ".xlsx"),
            _ => File(ExportHelper.GenerateCsv(headers, rows), "text/csv", fileNameBase + ".csv")
        };
    }
}
