using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Staff;

/// The front-of-house view of today: who arrives next, which tables are in use, what the kitchen has and what is waiting to be paid.
public class DashboardModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IReservationService _reservations;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardModel(ApplicationDbContext context, IReservationService reservations, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _reservations = reservations;
        _userManager = userManager;
    }

    public enum TableState { Free, Booked, Seated }

    public record Arrival(Reservation R, string Name, string Detail, List<string> Allergies, List<string> Needs, bool Due);
    public record FloorTable(RestaurantTable T, TableState State, string Title);
    public record FloorArea(string Name, List<FloorTable> Tables);

    public int ReservationsToday { get; private set; }
    public int SeatedNow { get; private set; }
    public int ToCome { get; private set; }
    public int TablesInUse { get; private set; }
    public int TablesTotal { get; private set; }
    public int BookedLater { get; private set; }
    public int OpenOrders { get; private set; }
    public int ReadyOrders { get; private set; }
    public int BillsToSettle { get; private set; }
    public decimal BillsAmount { get; private set; }
    public int GuestsToday { get; private set; }
    public List<Arrival> Next { get; private set; } = new();
    public List<FloorArea> Floor { get; private set; } = new();
    public List<Order> Kitchen { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var now = DateTime.Now;
        var start = DateTime.Today;
        var today = await _context.Reservations.AsNoTracking()
            .Include(r => r.Customer).Include(r => r.Table).Include(r => r.SetMenu)
            .Where(r => r.ReservationDateTime >= start && r.ReservationDateTime < start.AddDays(1))
            .OrderBy(r => r.ReservationDateTime).ToListAsync();
        var live = today.Where(r => r.Status is not (ReservationStatus.Cancelled or ReservationStatus.NoShow)).ToList();

        ReservationsToday = live.Count;
        GuestsToday = live.Sum(r => r.PartySize);
        SeatedNow = live.Count(r => r.Status == ReservationStatus.Seated);
        ToCome = live.Count(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed);

        var tables = await _context.RestaurantTables.AsNoTracking().Include(t => t.DiningArea)
            .Where(t => t.IsActive).ToListAsync();
        TablesTotal = tables.Count;
        var seatedIds = live.Where(r => r.Status == ReservationStatus.Seated && r.TableId.HasValue).Select(r => r.TableId!.Value).ToHashSet();
        var bookedIds = live.Where(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed && r.TableId.HasValue && r.ReservationEndDateTime > now)
            .Select(r => r.TableId!.Value).ToHashSet();
        TablesInUse = seatedIds.Count;
        BookedLater = bookedIds.Except(seatedIds).Count();

        Floor = tables
            .GroupBy(t => t.DiningArea?.Name ?? "Tables")
            .OrderBy(g => g.Key)
            .Select(g => new FloorArea(g.Key, g
                .OrderBy(t => t.PositionY ?? int.MaxValue).ThenBy(t => t.PositionX ?? int.MaxValue).ThenBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber)
                .Select(t =>
                {
                    var state = seatedIds.Contains(t.Id) ? TableState.Seated : bookedIds.Contains(t.Id) ? TableState.Booked : TableState.Free;
                    var title = $"{t.TableNumber}, seats {t.Capacity}: " + state switch { TableState.Seated => "seated", TableState.Booked => "booked", _ => "free" };
                    return new FloorTable(t, state, title);
                }).ToList()))
            .ToList();

        // anyone still to come, from half an hour ago onwards (a guest who is a little late is still "next")
        Next = live.Where(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed && r.ReservationDateTime >= now.AddMinutes(-30))
            .Take(6)
            .Select(r =>
            {
                var d = DietaryRules.SplitAllergies(r.Customer?.Allergies);
                var allergies = d.Chips.Select(DietaryRules.AllergenLabel).ToList();
                if (!string.IsNullOrWhiteSpace(d.Other)) allergies.Add(d.Other.Trim());
                var needs = DietaryRules.ParsePreferences(r.Customer?.DietaryPreference).Select(DietaryRules.NeedLabel).Take(2).ToList();
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(r.Occasion)) parts.Add(r.Occasion!);
                if (r.SetMenu != null) parts.Add("Set menu");
                return new Arrival(r, r.ContactName?.Trim() is { Length: > 0 } cn ? cn : r.Customer?.FullName ?? "Guest", string.Join(", ", parts), allergies, needs,
                    r.ReservationDateTime <= now.AddMinutes(20));
            }).ToList();

        var open = await _context.Orders.AsNoTracking()
            .Include(o => o.Table).Include(o => o.OrderItems).Include(o => o.Payment)
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Completed)
            .OrderBy(o => o.OrderDate).ToListAsync();
        Kitchen = open.Where(o => o.Status is OrderStatus.Placed or OrderStatus.Preparing or OrderStatus.ReadyToServe).ToList();
        OpenOrders = Kitchen.Count;
        ReadyOrders = Kitchen.Count(o => o.Status == OrderStatus.ReadyToServe);

        var unpaid = await _context.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled && (o.Payment == null || o.Payment.Status == PaymentStatus.Pending))
            .Select(o => o.TotalAmount).ToListAsync();
        BillsToSettle = unpaid.Count;
        BillsAmount = unpaid.Sum();
    }

    public async Task<IActionResult> OnPostSeatAsync(int id)
    {
        var (ok, msg) = await _reservations.UpdateStatusAsync(id, ReservationStatus.Seated, _userManager.GetUserId(User)!);
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = msg;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        var (ok, msg) = await _reservations.ConfirmReservationAsync(id, _userManager.GetUserId(User)!, null);
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = ok ? "Confirmed. The guest has been told." : msg;
        return RedirectToPage();
    }
}
