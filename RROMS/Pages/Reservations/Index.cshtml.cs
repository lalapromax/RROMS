using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Reservations;

/// <summary>One booking as the list shows it.</summary>
public record BookingCard(Reservation Booking, bool Reviewed, int? OrderId)
{
    public string Title =>
        $"{Booking.ReservationDateTime:dddd} {(Booking.ReservationDateTime.Hour < 17 ? "lunch" : "dinner")}";
}

public class IndexModel : PageModel
{
    private readonly IReservationService _reservations;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IReservationService reservations, ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _reservations = reservations;
        _context = context;
        _userManager = userManager;
    }

    /// <summary>upcoming (default), past or cancelled.</summary>
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "upcoming";

    public List<BookingCard> Upcoming { get; private set; } = new();
    public List<BookingCard> Past { get; private set; } = new();
    public List<BookingCard> Cancelled { get; private set; } = new();
    public List<WaitlistEntry> Waiting { get; private set; } = new();
    public List<DateTime> OpenDates { get; private set; } = new();
    public string AllergiesOnFile { get; private set; } = string.Empty;
    public string? Phone { get; private set; }

    /// <summary>On the "Upcoming" tab only the latest few past visits sit below the list; the rest are on the Past tab.</summary>
    public const int PastPreview = 3;

    public async Task OnGetAsync()
    {
        if (Tab is not ("upcoming" or "past" or "cancelled")) Tab = "upcoming";
        var user = await _userManager.GetUserAsync(User);
        var userId = user!.Id;

        var all = await _context.Reservations.AsNoTracking()
            .Include(r => r.Table)
            .Include(r => r.SetMenu)
            .Include(r => r.Orders)
            .Include(r => r.Feedbacks)
            .Where(r => r.CustomerId == userId)
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();

        BookingCard Card(Reservation r) =>
            new(r, r.Feedbacks.Any(), r.Orders.OrderByDescending(o => o.OrderDate).Select(o => (int?)o.Id).FirstOrDefault());

        var now = DateTime.Now;
        bool IsOpen(Reservation r) =>
            r.Status is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated && r.ReservationEndDateTime >= now;

        Upcoming = all.Where(IsOpen).Select(Card).ToList();
        Past = all.Where(r => r.Status != ReservationStatus.Cancelled && !IsOpen(r)).OrderByDescending(r => r.ReservationDateTime).Select(Card).ToList();
        Cancelled = all.Where(r => r.Status == ReservationStatus.Cancelled).OrderByDescending(r => r.ReservationDateTime).Select(Card).ToList();

        Waiting = await _context.WaitlistEntries.AsNoTracking()
            .Where(w => w.CustomerId == userId && w.NotifiedAt == null && w.WantedDateTime > now)
            .OrderBy(w => w.WantedDateTime)
            .ToListAsync();

        var allergies = DietaryRules.SplitAllergies(user.Allergies);
        var names = allergies.Chips.Select(DietaryRules.AllergenLabel).ToList();
        if (!string.IsNullOrWhiteSpace(allergies.Other)) names.Add(allergies.Other);
        AllergiesOnFile = string.Join(", ", names).ToLowerInvariant();

        var setting = await _context.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        Phone = setting?.Phone;
        var window = setting?.MaxAdvanceBookingDays ?? 60;
        var closed = (await _context.OperatingHours.AsNoTracking().Where(h => h.IsClosed).Select(h => h.DayOfWeek).ToListAsync()).ToHashSet();
        OpenDates = Enumerable.Range(0, window + 1).Select(i => DateTime.Today.AddDays(i)).Where(d => !closed.Contains(d.DayOfWeek)).ToList();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var (success, message) = await _reservations.CancelReservationAsync(id, userId, isStaffOrAdmin: false, reason: "Cancelled by the guest");
        TempData[success ? "StatusMessage" : "ErrorMessage"] = success ? "Your booking is cancelled." : message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangeAsync(int id, string? date, string? time, int party, string? notes)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || !TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var at)
            || party < 1 || party > CreateModel.MaxParty)
        {
            TempData["ErrorMessage"] = "Please choose a day, a time and the number of guests.";
            return RedirectToPage();
        }

        var userId = _userManager.GetUserId(User)!;
        var (success, message) = await _reservations.UpdateReservationAsync(id, userId, day, at, party, notes, isStaffOrAdmin: false);
        TempData[success ? "StatusMessage" : "ErrorMessage"] = success
            ? $"Your booking is now {day:dddd d MMMM} at {at:hh\\:mm}."
            : message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLeaveWaitlistAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var entry = await _context.WaitlistEntries.FirstOrDefaultAsync(w => w.Id == id && w.CustomerId == userId);
        if (entry != null)
        {
            _context.WaitlistEntries.Remove(entry);
            await _context.SaveChangesAsync();
        }
        TempData["StatusMessage"] = "You have left the waiting list.";
        return RedirectToPage();
    }
}
