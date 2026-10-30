using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Reservations;

/// What the booking form posts. Date, time and month travel as plain text so the page works without scripts.
public class BookForm
{
    [Range(1, CreateModel.MaxParty, ErrorMessage = "Choose between 1 and 8 guests. For more, please call us.")]
    public int PartySize { get; set; } = 2;

    /// yyyy-MM-dd
    public string? Date { get; set; }

    /// HH:mm
    public string? Time { get; set; }

    /// The month shown in the calendar, yyyy-MM.
    public string? Month { get; set; }

    [Required(ErrorMessage = "Please tell us who to ask for."), StringLength(80)]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Please add a number we can reach you on."), StringLength(30), RegularExpression(@"^[0-9+()\-\s]{6,30}$", ErrorMessage = "Use digits only, for example +60 12 345 6789.")]
    public string? Phone { get; set; }

    [StringLength(40)]
    public string? Occasion { get; set; }

    [StringLength(400)]
    public string? Notes { get; set; }

    public int? DiningAreaId { get; set; }

    /// The set menus ticked. Each can be for part of the table (<see cref="SetGuests"/>), so a booking can carry several.
    public List<int> SetMenuIds { get; set; } = new();

    /// How many guests have each ticked set menu, keyed by set menu id.
    public Dictionary<int, int> SetGuests { get; set; } = new();
}

/// One cell of the calendar: a date a guest can pick, or a blank/closed/past one.
public record CalendarDay(DateTime? Date, bool Open);

public class CreateModel : PageModel
{
    public const int MaxParty = 8;
    public static readonly string[] Occasions = { "A birthday", "An anniversary", "Business", "Just dinner" };

    private readonly IReservationService _reservations;
    private readonly ISetMenuService _setMenus;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(IReservationService reservations, ISetMenuService setMenus, ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _reservations = reservations;
        _setMenus = setMenus;
        _context = context;
        _userManager = userManager;
    }

    [BindProperty]
    public BookForm Form { get; set; } = new();

    /// Where to go once the table is booked, for example back to the cart. Only ever a page on this site.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public DateTime Month { get; private set; }
    public DateTime? SelectedDate { get; private set; }
    public List<CalendarDay> Calendar { get; private set; } = new();
    public bool CanGoBack { get; private set; }
    public bool CanGoForward { get; private set; }
    public DaySlots Slots { get; private set; } = new(false, Array.Empty<SlotInfo>());
    public List<DiningArea> DiningAreas { get; private set; } = new();
    public List<SetMenu> SetMenus { get; private set; } = new();
    public List<string> AllergiesOnFile { get; private set; } = new();
    public List<DayOfWeek> ClosedDays { get; private set; } = new();
    public int HoldMinutes { get; private set; } = 15;
    public string? Phone { get; private set; }
    public string? SelectedSlotNote { get; private set; }

    public bool SelectedSlotIsFull => Form.Time != null && Slots.Slots.Any(s => s.Time == Form.Time && s.Full);

    /// The set menus ticked so far, with how many guests have each.
    public List<(SetMenu Menu, int Guests)> ChosenSetMenus => SetMenus
        .Where(m => Form.SetMenuIds.Contains(m.Id))
        .Select(m => (m, GuestsFor(m.Id)))
        .ToList();

    /// Guests on one set menu: what was chosen, else the whole table, never more than the table.
    public int GuestsFor(int setMenuId) =>
        Math.Clamp(Form.SetGuests.TryGetValue(setMenuId, out var n) ? n : Form.PartySize, 1, Math.Max(1, Form.PartySize));

    public async Task OnGetAsync(int? party, string? date)
    {
        var user = await _userManager.GetUserAsync(User);
        Form.Name = user?.FullName;
        Form.Phone = user?.PhoneNumber;
        if (party is >= 1 and <= MaxParty) Form.PartySize = party.Value;
        if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Form.Date = d.ToString("yyyy-MM-dd");
        await PrepareAsync(user);
    }

    /// Used by the page's script: the start times for one day and party size, without reloading the page.
    public async Task<IActionResult> OnGetSlotsAsync(string date, int party, int? area)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || party < 1 || party > MaxParty)
            return BadRequest();
        var slots = await _reservations.GetSlotsAsync(d, party, area);
        return new JsonResult(new { closed = slots.Closed, slots = slots.Slots.Select(s => new { time = s.Time, full = s.Full }) });
    }

    /// Re-draws the page with what has been entered so far (month arrows, and the "show times" button when scripts are off).
    public async Task<IActionResult> OnPostRefreshAsync(string? nav)
    {
        ModelState.Clear();
        if (nav is "prev" or "next" && DateTime.TryParseExact(Form.Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var shown))
        {
            Form.Month = shown.AddMonths(nav == "next" ? 1 : -1).ToString("yyyy-MM");
            Form.Date = null; // the month changed, so the old day is no longer on screen
        }
        await PrepareAsync(await _userManager.GetUserAsync(User));
        return Page();
    }

    public async Task<IActionResult> OnPostBookAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (!TryReadSlot(out var date, out var time))
            ModelState.AddModelError(string.Empty, "Please choose a day and a time.");

        if (ModelState.IsValid)
        {
            var sets = Form.SetMenuIds.Distinct().Select(id => new SetMenuChoice(id, GuestsFor(id))).ToList();
            var (ok, message, reservation) = await _reservations.CreateReservationAsync(new ReservationRequest(
                user!.Id, date, time, Form.PartySize, Form.Notes, Form.DiningAreaId, null,
                Occasions.Contains(Form.Occasion) ? Form.Occasion : null, sets, Form.Name, Form.Phone));
            if (ok)
            {
                // The number given here is remembered on the account when it has none yet, so the next booking is filled in.
                if (string.IsNullOrWhiteSpace(user.PhoneNumber) && !string.IsNullOrWhiteSpace(Form.Phone))
                {
                    user.PhoneNumber = Form.Phone.Trim();
                    await _userManager.UpdateAsync(user);
                }

                var when = reservation!.ReservationDateTime;
                TempData["StatusMessage"] = reservation.Status == ReservationStatus.Pending
                    ? $"We have your request for {when:dddd d MMMM} at {when:HH:mm}. The team will confirm it within the day."
                    : $"Your table is booked for {when:dddd d MMMM} at {when:HH:mm}.";
                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                    return LocalRedirect(WithReservation(ReturnUrl, reservation.Id));
                return RedirectToPage("/Reservations/Index");
            }
            ModelState.AddModelError(string.Empty, message);
        }

        await PrepareAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostWaitlistAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (!TryReadSlot(out var date, out var time))
        {
            ModelState.AddModelError(string.Empty, "Please choose a day and a time.");
            await PrepareAsync(user);
            return Page();
        }

        var (ok, message) = await _reservations.JoinWaitlistAsync(user!.Id, date, time, Form.PartySize);
        if (ok)
        {
            TempData["StatusMessage"] = message;
            return RedirectToPage("/Reservations/Index");
        }
        ModelState.AddModelError(string.Empty, message);
        await PrepareAsync(user);
        return Page();
    }

    /// The return address with this booking selected. Any reservationId it already carried is replaced, not repeated,
    /// because a repeated key binds to its first value and the old booking would be chosen.
    private static string WithReservation(string url, int reservationId)
    {
        var hash = url.IndexOf('#');
        var fragment = hash >= 0 ? url[hash..] : "";
        var rest = hash >= 0 ? url[..hash] : url;
        var q = rest.IndexOf('?');
        var path = q >= 0 ? rest[..q] : rest;
        var query = QueryHelpers.ParseQuery(q >= 0 ? rest[q..] : "")
            .Where(p => !p.Key.Equals("reservationId", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string?>(p.Key, v)))
            .Append(new KeyValuePair<string, string?>("reservationId", reservationId.ToString()));
        return QueryHelpers.AddQueryString(path, query) + fragment;
    }

    private bool TryReadSlot(out DateTime date, out TimeSpan time)
    {
        time = default;
        var okDate = DateTime.TryParseExact(Form.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        var okTime = TimeSpan.TryParseExact(Form.Time, @"hh\:mm", CultureInfo.InvariantCulture, out time);
        return okDate && okTime;
    }

    private async Task PrepareAsync(ApplicationUser? user)
    {
        var setting = await _context.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync();
        HoldMinutes = setting?.HoldMinutes ?? 15;
        Phone = setting?.PublicPhone;
        var maxAdvance = setting?.MaxAdvanceBookingDays ?? 60;

        var hours = await _context.OperatingHours.AsNoTracking().ToListAsync();
        ClosedDays = hours.Where(h => h.IsClosed).Select(h => h.DayOfWeek).ToList();

        DiningAreas = await _context.DiningAreas.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync();
        if (Form.DiningAreaId.HasValue && DiningAreas.All(a => a.Id != Form.DiningAreaId)) Form.DiningAreaId = null;

        var allergies = DietaryRules.SplitAllergies(user?.Allergies);
        AllergiesOnFile = allergies.Chips.Select(DietaryRules.AllergenLabel).ToList();
        if (!string.IsNullOrWhiteSpace(allergies.Other)) AllergiesOnFile.Add(allergies.Other);

        // The calendar: today to today + the booking window, minus the days the restaurant is closed.
        var today = DateTime.Today;
        var last = today.AddDays(maxAdvance);
        bool IsOpen(DateTime d) => d >= today && d <= last && !ClosedDays.Contains(d.DayOfWeek);

        DateTime? picked = null;
        if (DateTime.TryParseExact(Form.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var posted) && IsOpen(posted))
            picked = posted.Date;
        for (var i = 0; picked == null && i <= maxAdvance; i++)
            if (IsOpen(today.AddDays(i))) picked = today.AddDays(i);
        SelectedDate = picked;
        Form.Date = picked?.ToString("yyyy-MM-dd");

        Month = DateTime.TryParseExact(Form.Month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
            ? new DateTime(m.Year, m.Month, 1)
            : new DateTime((picked ?? today).Year, (picked ?? today).Month, 1);
        var firstMonth = new DateTime(today.Year, today.Month, 1);
        var lastMonth = new DateTime(last.Year, last.Month, 1);
        if (Month < firstMonth) Month = firstMonth;
        if (Month > lastMonth) Month = lastMonth;
        Form.Month = Month.ToString("yyyy-MM");
        CanGoBack = Month > firstMonth;
        CanGoForward = Month < lastMonth;

        Calendar = new List<CalendarDay>();
        var lead = ((int)Month.DayOfWeek + 6) % 7;                // weeks start on Monday
        for (var i = 0; i < lead; i++) Calendar.Add(new CalendarDay(null, false));
        for (var day = Month; day.Month == Month.Month; day = day.AddDays(1))
            Calendar.Add(new CalendarDay(day, IsOpen(day)));

        Slots = picked.HasValue
            ? await _reservations.GetSlotsAsync(picked.Value, Form.PartySize, Form.DiningAreaId)
            : new DaySlots(true, Array.Empty<SlotInfo>());
        if (Form.Time != null && Slots.Slots.All(s => s.Time != Form.Time)) Form.Time = null;

        SetMenus = (await _setMenus.ListForGuestsAsync(picked?.DayOfWeek)).ToList();
        Form.SetMenuIds = Form.SetMenuIds.Where(id => SetMenus.Any(s => s.Id == id)).Distinct().ToList();
    }
}
