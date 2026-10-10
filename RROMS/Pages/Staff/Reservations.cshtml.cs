using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Staff;

/// 
/// The day's bookings: confirm requests, seat guests, change tables, take a booking or a walk-in for someone without an account.
/// Every action is a small post that goes back to the same day and filters. The rules (a table must be big enough and free for the
/// whole sitting) are enforced in <see cref="IReservationService"/>, so the page only offers what is likely to work.
/// 
public class ReservationsModel : PageModel
{
    private readonly IReservationService _reservations;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public ReservationsModel(IReservationService reservations, UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _reservations = reservations;
        _userManager = userManager;
        _context = context;
    }

    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? TableId { get; set; }
    /// The dashboard's "Add walk-in" opens the new-booking dialog with "seat now" ticked.
    [BindProperty(SupportsGet = true)] public bool WalkIn { get; set; }

    [BindProperty] public NewBookingInput New { get; set; } = new();
    [BindProperty] public EditBookingInput Edit { get; set; } = new();

    public DateTime Day => (Date ?? DateTime.Today).Date;
    public List<Row> Rows { get; private set; } = new();
    public List<RestaurantTable> AllTables { get; private set; } = new();
    public int GuestCount { get; private set; }
    public bool OpenNew { get; private set; }
    public bool OpenEdit { get; private set; }

    public static readonly (string Value, string Label)[] StatusChoices =
    {
        ("", "All"), ("pending", "Pending"), ("confirmed", "Confirmed"), ("seated", "Seated"),
        ("completed", "Finished"), ("cancelled", "Cancelled"), ("noshow", "No-show")
    };

    public record Row(Reservation R, string Name, string? Phone, string Detail, List<string> Allergies, List<string> Needs,
                      List<RestaurantTable> TableChoices, bool Due);

    public class NewBookingInput
    {
        public string? GuestName { get; set; }
        public string? Phone { get; set; }
        public DateTime? Date { get; set; }
        public string? Time { get; set; }
        public int PartySize { get; set; } = 2;
        public int? TableId { get; set; }
        public string? Occasion { get; set; }
        public string? Notes { get; set; }
        public bool SeatNow { get; set; }
    }

    public class EditBookingInput
    {
        public int Id { get; set; }
        public string? GuestName { get; set; }
        public DateTime? Date { get; set; }
        public string? Time { get; set; }
        public int PartySize { get; set; } = 2;
        public string? Notes { get; set; }
    }

    public async Task OnGetAsync()
    {
        if (WalkIn)
        {
            OpenNew = true;
            New.SeatNow = true;
            New.Date = DateTime.Today;
        }
        else
        {
            New.Date = DateTime.Today;
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var day = Day;
        var all = await _reservations.GetReservationsForDateAsync(day);
        AllTables = await _context.RestaurantTables.AsNoTracking().Include(t => t.DiningArea)
            .Where(t => t.IsActive).OrderBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber).ToListAsync();

        var open = all.Where(r => r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated).ToList();
        IEnumerable<Reservation> shown = all;

        var status = (Status ?? string.Empty).ToLowerInvariant();
        if (status.Length > 0)
        {
            ReservationStatus? wanted = status switch
            {
                "pending" => ReservationStatus.Pending, "confirmed" => ReservationStatus.Confirmed, "seated" => ReservationStatus.Seated,
                "completed" => ReservationStatus.Completed, "cancelled" => ReservationStatus.Cancelled, "noshow" => ReservationStatus.NoShow,
                _ => null
            };
            if (wanted.HasValue) shown = shown.Where(r => r.Status == wanted.Value);
        }
        if (TableId.HasValue) shown = shown.Where(r => r.TableId == TableId.Value);
        var q = (Q ?? string.Empty).Trim();
        if (q.Length > 0)
        {
            shown = shown.Where(r =>
                Contains(r.ContactName, q) || Contains(r.Customer?.FullName, q) || Contains(r.ContactPhone, q) ||
                Contains(r.Customer?.PhoneNumber, q) || Contains(r.Table?.TableNumber, q));
        }

        var now = DateTime.Now;
        foreach (var r in shown.OrderBy(r => r.ReservationDateTime).ThenBy(r => r.Id))
        {
            var diet = DietaryRules.SplitAllergies(r.Customer?.Allergies);
            var allergies = diet.Chips.Select(DietaryRules.AllergenLabel).ToList();
            if (!string.IsNullOrWhiteSpace(diet.Other)) allergies.Add(diet.Other.Trim());
            var needs = DietaryRules.ParsePreferences(r.Customer?.DietaryPreference).Select(DietaryRules.NeedLabel).Take(2).ToList();

            var detail = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Occasion)) detail.Add(r.Occasion!);
            if (r.SetMenu != null) detail.Add("Set menu: " + r.SetMenu.Name);
            if (!string.IsNullOrWhiteSpace(r.SpecialRequests)) detail.Add(r.SpecialRequests!);

            var choices = new List<RestaurantTable>();
            if (r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated)
            {
                var end = r.ReservationDateTime.AddMinutes(r.DurationMinutes);
                choices = AllTables.Where(t => t.Id == r.TableId || (t.Capacity >= r.PartySize &&
                    !open.Any(o => o.Id != r.Id && o.TableId == t.Id && o.ReservationDateTime < end && r.ReservationDateTime < o.ReservationDateTime.AddMinutes(o.DurationMinutes)))).ToList();
            }

            Rows.Add(new Row(r, r.ContactName?.Trim() is { Length: > 0 } cn ? cn : r.Customer?.FullName ?? "Guest",
                string.IsNullOrWhiteSpace(r.ContactPhone) ? r.Customer?.PhoneNumber : r.ContactPhone,
                string.Join(", ", detail), allergies, needs, choices, r.ReservationDateTime <= now.AddMinutes(20)));
        }
        GuestCount = Rows.Where(x => x.R.Status is not (ReservationStatus.Cancelled or ReservationStatus.NoShow)).Sum(x => x.R.PartySize);
    }

    private static bool Contains(string? hay, string needle) => hay != null && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private IActionResult Back() => RedirectToPage(new
    {
        date = Day == DateTime.Today ? null : Day.ToString("yyyy-MM-dd"),
        status = string.IsNullOrEmpty(Status) ? null : Status,
        q = string.IsNullOrWhiteSpace(Q) ? null : Q,
        tableId = TableId
    });

    private IActionResult Done(bool success, string message)
    {
        TempData[success ? "StatusMessage" : "ErrorMessage"] = message;
        return Back();
    }

    private string StaffId => _userManager.GetUserId(User)!;

    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        var (ok, msg) = await _reservations.ConfirmReservationAsync(id, StaffId, null);
        return Done(ok, ok ? "Confirmed. The guest has been told." : msg);
    }

    public async Task<IActionResult> OnPostSeatAsync(int id)
    {
        var (ok, msg) = await _reservations.UpdateStatusAsync(id, ReservationStatus.Seated, StaffId);
        return Done(ok, msg);
    }

    public async Task<IActionResult> OnPostFinishAsync(int id)
    {
        var (ok, msg) = await _reservations.UpdateStatusAsync(id, ReservationStatus.Completed, StaffId);
        return Done(ok, msg);
    }

    public async Task<IActionResult> OnPostNoShowAsync(int id)
    {
        var (ok, msg) = await _reservations.UpdateStatusAsync(id, ReservationStatus.NoShow, StaffId);
        return Done(ok, ok ? "Marked as a no-show." : msg);
    }

    public async Task<IActionResult> OnPostReopenAsync(int id)
    {
        var (ok, msg) = await _reservations.ReopenAsync(id, StaffId);
        return Done(ok, msg);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int tableId)
    {
        var (ok, msg) = await _reservations.MoveTableAsync(id, tableId);
        return Done(ok, msg);
    }

    /// "Decline" for a request, "Cancel" for anything else: the guest is told, and sees the reason if one was given.
    public async Task<IActionResult> OnPostDeclineAsync(int id, string? reason)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by staff" : reason.Trim();
        if (reason.Length > 300) return Done(false, "Keep the reason under 300 characters.");
        var (ok, msg) = await _reservations.CancelReservationAsync(id, StaffId, isStaffOrAdmin: true, reason);
        return Done(ok, ok ? "Cancelled. The guest has been told." : msg);
    }

    public async Task<IActionResult> OnPostEditAsync()
    {
        var res = await _reservations.GetByIdAsync(Edit.Id);
        if (res == null) return Done(false, "Reservation not found.");

        var error = ValidateWhen(Edit.Date, Edit.Time, Edit.PartySize, out var time);
        if (error == null)
        {
            var (ok, msg) = await _reservations.UpdateReservationAsync(res.Id, res.CustomerId, Edit.Date!.Value, time, Edit.PartySize, Edit.Notes, isStaffOrAdmin: true);
            if (ok) return Done(true, "Booking updated. The guest has been told.");
            error = msg;
        }

        ModelState.AddModelError("Edit", error);
        OpenEdit = true;
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var error = New.SeatNow ? null : ValidateWhen(New.Date, New.Time, New.PartySize, out _);
        if (New.SeatNow && (New.PartySize < 1 || New.PartySize > 30)) error = "Party size must be between 1 and 30.";
        if (error == null)
        {
            TimeSpan.TryParse(New.Time, out var t);
            var (ok, msg, _) = await _reservations.CreateByStaffAsync(
                new StaffBooking(New.GuestName ?? "", New.Phone, New.Date ?? DateTime.Today, t, New.PartySize, New.TableId, New.Notes, New.Occasion, New.SeatNow), StaffId);
            if (ok)
            {
                if (!New.SeatNow && New.Date.HasValue) Date = New.Date.Value;
                return Done(true, msg);
            }
            error = msg;
        }

        ModelState.AddModelError("New", error);
        OpenNew = true;
        await LoadAsync();
        return Page();
    }

    private static string? ValidateWhen(DateTime? date, string? time, int party, out TimeSpan t)
    {
        t = default;
        if (!date.HasValue) return "Please choose the day.";
        if (!TimeSpan.TryParse(time, out t)) return "Please choose the time.";
        if (party < 1 || party > 30) return "Party size must be between 1 and 30.";
        return null;
    }
}
