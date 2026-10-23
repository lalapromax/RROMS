using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// 
/// A plain record of what happened, for checking and for the accountant: reservations, orders and payments over a chosen
/// stretch of days, with a download of whichever list is showing. Reservations are kept in local time and orders and
/// payments in UTC, so the chosen days are turned into the right bounds for each. Read-only, administrators only
/// (the /Admin folder policy in Program.cs).
/// 
public class MonitoringModel : PageModel
{
    public const int PageSize = 50;
    private const int MaxDays = 400;
    private const int CsvRowLimit = 20000;

    private readonly ApplicationDbContext _context;
    private readonly IAuditService _audit;
    private readonly UserManager<ApplicationUser> _userManager;

    public MonitoringModel(ApplicationDbContext context, IAuditService audit, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _audit = audit;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    /// "reservations" (default), "orders" or "payments".
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    /// A status of whichever list is showing; blank means all.
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;

    public static readonly (string Key, string Label)[] Tabs = { ("reservations", "Reservations"), ("orders", "Orders"), ("payments", "Payments") };

    public record ResRow(string Guest, string When, int Party, string Table, string Status, string Css);
    public record OrdRow(int Id, string Guest, string When, string Type, decimal Total, string Status, string Css);
    public record PayRow(int OrderId, string Guest, string When, decimal Amount, string Method, string Status, string Css, string? Reference);

    public List<ResRow> Reservations { get; private set; } = new();
    public List<OrdRow> Orders { get; private set; } = new();
    public List<PayRow> Payments { get; private set; } = new();

    public Dictionary<string, int> Counts { get; private set; } = new();
    public int Total => Counts.GetValueOrDefault(Tab ?? "reservations");
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public List<(string Value, string Label)> StatusChoices { get; private set; } = new();

    // The four figures at the top (for the days chosen, whatever the status filter says).
    public int ResCount { get; private set; }
    public int OrdCount { get; private set; }
    public decimal PaidSum { get; private set; }
    public int LostCount { get; private set; }
    public int FailedCount { get; private set; }
    public int Days { get; private set; }
    public string? RangeNote { get; private set; }

    public string FromText => From!.Value.ToString("yyyy-MM-dd");
    public string ToText => To!.Value.ToString("yyyy-MM-dd");

    public async Task OnGetAsync()
    {
        Normalise();
        await LoadAsync();
    }

    /// The list that is showing, as a spreadsheet file.
    public async Task<IActionResult> OnGetDownloadAsync()
    {
        Normalise();
        var (fromLocal, toLocal, fromUtc, toUtc) = Bounds();
        var sb = new StringBuilder();
        sb.Append('﻿'); // lets Excel read the accents in names

        switch (Tab)
        {
            case "orders":
            {
                sb.AppendLine("Order,Guest,Placed,Type,Total,Status");
                var q = OrderQuery(fromUtc, toUtc).Include(o => o.Customer).AsNoTracking().OrderByDescending(o => o.OrderDate).Take(CsvRowLimit);
                foreach (var o in await q.ToListAsync())
                    sb.AppendLine(Csv(o.Id.ToString(CultureInfo.InvariantCulture), o.Customer?.FullName, Stamp(o.OrderDate), TypeLabel(o.OrderType),
                        o.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture), OrderLabel(o.Status)));
                break;
            }
            case "payments":
            {
                sb.AppendLine("Order,Guest,Taken,Amount,Method,Status,Reference");
                var q = PaymentQuery(fromUtc, toUtc).Include(p => p.Order).ThenInclude(o => o!.Customer).AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(CsvRowLimit);
                foreach (var p in await q.ToListAsync())
                    sb.AppendLine(Csv(p.OrderId.ToString(CultureInfo.InvariantCulture), p.Order?.Customer?.FullName, Stamp(p.CreatedAt),
                        p.Amount.ToString("0.00", CultureInfo.InvariantCulture), MethodLabel(p.Method), PayLabel(p.Status), p.TransactionReference));
                break;
            }
            default:
            {
                sb.AppendLine("Guest,Date and time,Guests,Table,Status");
                var q = ReservationQuery(fromLocal, toLocal).Include(r => r.Customer).Include(r => r.Table).AsNoTracking().OrderByDescending(r => r.ReservationDateTime).Take(CsvRowLimit);
                foreach (var r in await q.ToListAsync())
                    sb.AppendLine(Csv(GuestName(r), r.ReservationDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        r.PartySize.ToString(CultureInfo.InvariantCulture), r.Table?.TableNumber, ResLabel(r.Status)));
                break;
            }
        }

        var admin = await _userManager.GetUserAsync(User);
        if (admin != null)
            await _audit.LogAsync(admin.Id, admin.FullName, "ExportMonitoring", $"{Tab}, {FromText} to {ToText}");

        var name = $"monitoring-{Tab}-{FromText}-to-{ToText}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", name);
    }

    // ---------------------------------------------------------------------------------------------------------------

    private void Normalise()
    {
        var today = DateTime.Today;
        To = (To ?? today).Date;
        From = (From ?? To.Value.AddDays(-29)).Date;
        if (From > To) (From, To) = (To, From);
        if ((To.Value - From.Value).TotalDays > MaxDays)
        {
            From = To.Value.AddDays(-MaxDays);
            RangeNote = $"That is a long stretch. Showing the last {MaxDays} days of it.";
        }
        Days = (int)(To.Value - From.Value).TotalDays + 1;
        Tab = Tabs.Any(t => t.Key == Tab) ? Tab : "reservations";
        if (P < 1) P = 1;
        Status = string.IsNullOrWhiteSpace(Status) ? null : Status.Trim();
    }

    private (DateTime FromLocal, DateTime ToLocal, DateTime FromUtc, DateTime ToUtc) Bounds()
    {
        var fromLocal = From!.Value.Date;
        var toLocal = To!.Value.Date.AddDays(1);
        return (fromLocal, toLocal, TimeInput.LocalToUtc(fromLocal)!.Value, TimeInput.LocalToUtc(toLocal)!.Value);
    }

    private IQueryable<Reservation> ReservationQuery(DateTime fromLocal, DateTime toLocal, bool withStatus = true)
    {
        var q = _context.Reservations.Where(r => r.ReservationDateTime >= fromLocal && r.ReservationDateTime < toLocal);
        if (withStatus && Tab == "reservations" && Status != null && Enum.TryParse<ReservationStatus>(Status, true, out var s)) q = q.Where(r => r.Status == s);
        return q;
    }

    private IQueryable<Order> OrderQuery(DateTime fromUtc, DateTime toUtc, bool withStatus = true)
    {
        var q = _context.Orders.Where(o => o.OrderDate >= fromUtc && o.OrderDate < toUtc);
        if (withStatus && Tab == "orders" && Status != null && Enum.TryParse<OrderStatus>(Status, true, out var s)) q = q.Where(o => o.Status == s);
        return q;
    }

    private IQueryable<Payment> PaymentQuery(DateTime fromUtc, DateTime toUtc, bool withStatus = true)
    {
        var q = _context.Payments.Where(p => p.CreatedAt >= fromUtc && p.CreatedAt < toUtc);
        if (withStatus && Tab == "payments" && Status != null && Enum.TryParse<PaymentStatus>(Status, true, out var s)) q = q.Where(p => p.Status == s);
        return q;
    }

    private async Task LoadAsync()
    {
        var (fromLocal, toLocal, fromUtc, toUtc) = Bounds();

        // The figures at the top ignore the status filter, so they always describe the whole stretch.
        ResCount = await ReservationQuery(fromLocal, toLocal, false).CountAsync();
        LostCount = await ReservationQuery(fromLocal, toLocal, false).CountAsync(r => r.Status == ReservationStatus.Cancelled || r.Status == ReservationStatus.NoShow);
        OrdCount = await OrderQuery(fromUtc, toUtc, false).CountAsync();
        PaidSum = await PaymentQuery(fromUtc, toUtc, false).Where(p => p.Status == PaymentStatus.Paid).SumAsync(p => (decimal?)p.Amount) ?? 0m;
        FailedCount = await PaymentQuery(fromUtc, toUtc, false).CountAsync(p => p.Status == PaymentStatus.Failed);

        Counts = new Dictionary<string, int>
        {
            ["reservations"] = await ReservationQuery(fromLocal, toLocal).CountAsync(),
            ["orders"] = await OrderQuery(fromUtc, toUtc).CountAsync(),
            ["payments"] = await PaymentQuery(fromUtc, toUtc).CountAsync()
        };
        if (P > Pages) P = Pages;
        var skip = (P - 1) * PageSize;

        switch (Tab)
        {
            case "orders":
                StatusChoices = Enum.GetValues<OrderStatus>().Select(s => (s.ToString(), OrderLabel(s))).ToList();
                Orders = (await OrderQuery(fromUtc, toUtc).Include(o => o.Customer).AsNoTracking()
                        .OrderByDescending(o => o.OrderDate).Skip(skip).Take(PageSize).ToListAsync())
                    .Select(o => new OrdRow(o.Id, o.Customer?.FullName ?? "Guest", LocalStamp(o.OrderDate), TypeLabel(o.OrderType), o.TotalAmount, OrderLabel(o.Status), OrderCss(o.Status)))
                    .ToList();
                break;
            case "payments":
                StatusChoices = Enum.GetValues<PaymentStatus>().Select(s => (s.ToString(), PayLabel(s))).ToList();
                Payments = (await PaymentQuery(fromUtc, toUtc).Include(p => p.Order).ThenInclude(o => o!.Customer).AsNoTracking()
                        .OrderByDescending(p => p.CreatedAt).Skip(skip).Take(PageSize).ToListAsync())
                    .Select(p => new PayRow(p.OrderId, p.Order?.Customer?.FullName ?? "Guest", LocalStamp(p.CreatedAt), p.Amount, MethodLabel(p.Method), PayLabel(p.Status), PayCss(p.Status), p.TransactionReference))
                    .ToList();
                break;
            default:
                StatusChoices = Enum.GetValues<ReservationStatus>().Select(s => (s.ToString(), ResLabel(s))).ToList();
                Reservations = (await ReservationQuery(fromLocal, toLocal).Include(r => r.Customer).Include(r => r.Table).AsNoTracking()
                        .OrderByDescending(r => r.ReservationDateTime).Skip(skip).Take(PageSize).ToListAsync())
                    .Select(r => new ResRow(GuestName(r), Casual(r.ReservationDateTime), r.PartySize,
                        r.Table?.TableNumber ?? "None yet", ResLabel(r.Status), ResCss(r.Status)))
                    .ToList();
                break;
        }
    }

    // ---- wording and pill colours, one place ----
    private static string GuestName(Reservation r) => !string.IsNullOrWhiteSpace(r.ContactName) ? r.ContactName! : r.Customer?.FullName ?? "Guest";
    private static string LocalStamp(DateTime utc) => Casual(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime());
    private static string Casual(DateTime local) => local.ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture).Replace("AM", "am").Replace("PM", "pm");
    private static string Stamp(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string ResLabel(ReservationStatus s) => s == ReservationStatus.NoShow ? "No-show" : s.ToString();
    public static string ResCss(ReservationStatus s) => "st-" + s.ToString().ToLowerInvariant();
    public static string OrderLabel(OrderStatus s) => s == OrderStatus.ReadyToServe ? "Ready to serve" : s.ToString();
    public static string OrderCss(OrderStatus s) => s == OrderStatus.ReadyToServe ? "st-ready" : "st-" + s.ToString().ToLowerInvariant();
    public static string TypeLabel(OrderType t) => t == OrderType.DineIn ? "Dine in" : "Takeaway";
    public static string PayLabel(PaymentStatus s) => s.ToString();
    public static string PayCss(PaymentStatus s) => s switch
    {
        PaymentStatus.Paid => "st-paid",
        PaymentStatus.Pending => "st-pending",
        PaymentStatus.Failed => "st-declined",
        _ => "st-completed"
    };
    public static string MethodLabel(PaymentMethod m) => m switch
    {
        PaymentMethod.OnlinePayment => "Card, online",
        PaymentMethod.OnlineBanking => "Online banking",
        PaymentMethod.Card => "Card",
        PaymentMethod.Cash => "Cash",
        _ => m.ToString()
    };

    // ---- spreadsheet file ----
    /// One CSV line. Anything a spreadsheet could read as a formula (starting = + - @) gets a quote in front, so a guest's name can never run as one.
    private static string Csv(params string?[] cells) => string.Join(",", cells.Select(Cell));

    private static string Cell(string? value)
    {
        value ??= "";
        if (value.Length > 0 && (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
