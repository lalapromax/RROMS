using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

[RequestSizeLimit(6 * 1024 * 1024)] // uploads are capped at 4 MB by IAdminImageService; this just stops oversized bodies early
public class VenueModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IReservationService _reservationService;
    private readonly IAdminImageService _images;

    public VenueModel(ApplicationDbContext context, IReservationService reservationService, IAdminImageService images)
    {
        _context = context;
        _reservationService = reservationService;
        _images = images;
    }

    /// Fixed floor-plan grid size, shared by the server (validation) and
    /// the client (rendering) - see wwwroot/js/floor-plan.js.
    public const int GridCols = 10;
    public const int GridRows = 6;

    /// "plan" (default), "tables" or "areas".
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    /// "edit" opens the plan in Edit layout, which is where a table that was just added is waiting in the tray.
    [BindProperty(SupportsGet = true)] public string? Mode { get; set; }
    /// The dining area the plan opens on.
    [BindProperty(SupportsGet = true)] public int? Area { get; set; }

    public List<DiningArea> DiningAreas { get; set; } = new();
    public List<RestaurantTable> Tables { get; set; } = new();

    public async Task OnGetAsync()
    {
        Tab = Tab is "tables" or "areas" ? Tab : "plan";
        await LoadAsync();
        if (Area == null || DiningAreas.All(a => a.Id != Area))
            Area = DiningAreas.Where(a => a.IsActive).Select(a => (int?)a.Id).FirstOrDefault() ?? DiningAreas.Select(a => (int?)a.Id).FirstOrDefault();
    }

    private async Task LoadAsync()
    {
        DiningAreas = await _context.DiningAreas.OrderBy(d => d.Name).ToListAsync();
        Tables = await _context.RestaurantTables.Include(t => t.DiningArea).OrderBy(t => t.DiningAreaId).ThenBy(t => t.TableNumber).ToListAsync();
        // 2 before 10: numbers sort the way people read them
        Tables = Tables.OrderBy(t => t.DiningArea?.Name).ThenBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IActionResult Back(string tab, int? area = null, bool edit = false) =>
        RedirectToPage(new { tab, area, mode = edit ? "edit" : null });

    private IActionResult Fail(string tab, string message)
    {
        TempData["ErrorMessage"] = message;
        return Back(tab);
    }

    // ---------------- Dining areas ----------------

    private static string? ValidateArea(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) return "Please enter an area name (up to 80 characters).";
        if (description != null && description.Length > 300) return "Keep the description under 300 characters.";
        return null;
    }

    /// Adds a dining area (id 0) or changes one.
    public async Task<IActionResult> OnPostSaveAreaAsync(int id, string? name, string? description, string? imageUrl, IFormFile? imageFile, bool isActive)
    {
        var problem = ValidateArea(name, description);
        if (problem != null) return Fail("areas", problem);

        var trimmed = name!.Trim();
        if (await _context.DiningAreas.AnyAsync(d => d.Id != id && d.Name.ToLower() == trimmed.ToLower()))
            return Fail("areas", $"There is already a dining area called \"{trimmed}\".");

        var area = id == 0 ? null : await _context.DiningAreas.FindAsync(id);
        if (id != 0 && area == null) return Fail("areas", "Dining area not found.");

        var (imageOk, imageMessage, resolvedImage) = await _images.ResolveAsync(imageFile, string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim(), "dining");
        if (!imageOk) return Fail("areas", imageMessage ?? "That photo could not be used.");

        if (area == null)
        {
            _context.DiningAreas.Add(new DiningArea { Name = trimmed, Description = description?.Trim(), ImageUrl = resolvedImage, IsActive = true });
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Dining area added.";
            return Back("areas");
        }

        var previousImage = area.ImageUrl;
        area.Name = trimmed;
        area.Description = description?.Trim();
        area.ImageUrl = resolvedImage;
        area.IsActive = isActive;
        await _context.SaveChangesAsync();
        if (previousImage != resolvedImage) _images.DeleteIfOurs(previousImage);

        TempData["StatusMessage"] = "Dining area updated.";
        return Back("areas");
    }

    public async Task<IActionResult> OnPostDeleteAreaAsync(int id)
    {
        if (await _context.RestaurantTables.AnyAsync(t => t.DiningAreaId == id))
            return Fail("areas", "This dining area still has tables. Move or remove them first, or switch the area off to hide it.");

        var area = await _context.DiningAreas.FindAsync(id);
        if (area != null)
        {
            _context.DiningAreas.Remove(area);
            await _context.SaveChangesAsync();
            _images.DeleteIfOurs(area.ImageUrl);
            TempData["StatusMessage"] = "Dining area removed.";
        }
        return Back("areas");
    }

    // ---------------- Tables ----------------

    /// 
    /// Server-side rules for a table: a short unique number, a sensible capacity, a dining area that exists, and (when an existing
    /// table gets fewer seats) no booking that no longer fits.
    /// 
    private async Task<string?> ValidateTableAsync(string? tableNumber, int diningAreaId, int capacity, int? ignoreTableId)
    {
        var number = tableNumber?.Trim();
        if (string.IsNullOrEmpty(number) || number.Length > 20)
            return "Please enter a table number (up to 20 characters).";
        if (capacity < 1 || capacity > 30)
            return "Seats must be between 1 and 30.";
        if (!await _context.DiningAreas.AnyAsync(d => d.Id == diningAreaId))
            return "Please choose a dining area.";

        var clash = await _context.RestaurantTables.AnyAsync(t =>
            t.TableNumber.ToLower() == number.ToLower() && (ignoreTableId == null || t.Id != ignoreTableId));
        if (clash)
            return $"There is already a table numbered \"{number}\".";

        if (ignoreTableId != null)
        {
            var now = DateTime.Now;
            var tooBig = await _context.Reservations.AsNoTracking()
                .Where(r => r.TableId == ignoreTableId && r.ReservationDateTime >= now && r.PartySize > capacity
                            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
                .OrderBy(r => r.ReservationDateTime).FirstOrDefaultAsync();
            if (tooBig != null)
                return $"A booking for {tooBig.PartySize} is on this table on {tooBig.ReservationDateTime:d MMM} at {tooBig.ReservationDateTime:HH:mm}. Move that booking first, or keep at least {tooBig.PartySize} seats.";
        }
        return null;
    }

    /// Adds a table (id 0) or changes one from the Tables tab. A new table waits in the tray of the plan until it is placed.
    public async Task<IActionResult> OnPostSaveTableAsync(int id, string? tableNumber, int diningAreaId, int capacity, bool isActive)
    {
        var problem = await ValidateTableAsync(tableNumber, diningAreaId, capacity, id == 0 ? null : id);
        if (problem != null) return Fail("tables", problem);

        if (id == 0)
        {
            _context.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = tableNumber!.Trim(),
                DiningAreaId = diningAreaId,
                Capacity = capacity,
                IsActive = true
            });
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Table added. It is waiting in the tray. Drag it onto the plan.";
            return Back("plan", diningAreaId, edit: true);
        }

        var table = await _context.RestaurantTables.FindAsync(id);
        if (table == null) return Fail("tables", "Table not found.");

        // A floor-plan position only makes sense within the dining area it was placed in - moving a table to a
        // different hall clears its position so it doesn't silently reappear at the same grid cell in a space it
        // was never actually placed in (and might already be occupied there).
        if (table.DiningAreaId != diningAreaId)
        {
            table.PositionX = null;
            table.PositionY = null;
        }

        table.TableNumber = tableNumber!.Trim();
        table.DiningAreaId = diningAreaId;
        table.Capacity = capacity;
        table.IsActive = isActive;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Table updated.";
        return Back("tables");
    }

    public async Task<IActionResult> OnPostDeleteTableAsync(int id)
    {
        var table = await _context.RestaurantTables.FindAsync(id);
        if (table != null)
        {
            var hasUpcoming = await _context.Reservations.AnyAsync(r => r.TableId == id &&
                r.ReservationDateTime >= DateTime.Now && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed));
            if (hasUpcoming)
                return Fail("tables", "This table has upcoming reservations. Move or cancel them first, or switch the table off instead.");

            _context.RestaurantTables.Remove(table);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Table removed.";
        }
        return Back("tables");
    }

    // ---------------- Visual floor plan ----------------
    // These handlers are called from wwwroot/js/floor-plan.js via fetch, not from an ordinary form post, so they
    // return JSON instead of redirecting. Every check here runs again on the server - the browser's own grid-snap
    // and collision highlighting are just a preview, never the real guard.

    public async Task<IActionResult> OnGetTableStatusesAsync(int diningAreaId, DateTime date, string time)
    {
        if (!TimeSpan.TryParse(time, out var parsedTime))
            return new JsonResult(new { success = false, message = "Invalid time." });

        var statuses = await _reservationService.GetTableStatusesAsync(date, parsedTime, diningAreaId);
        return new JsonResult(new { success = true, statuses });
    }

    /// 
    /// What the floor plan shows. With no date/time it means "right now", so a table that is booked at this moment is
    /// marked without the admin having to set anything. A booking counts when it overlaps the window
    /// [checked time, checked time + reservation length) - the same rule the booking engine uses - so the plan and
    /// what customers can actually reserve never disagree.
    /// 
    public async Task<IActionResult> OnGetFloorStatusAsync(int diningAreaId, string? date, string? time)
    {
        var now = DateTime.Now;
        var at = now;
        var isNow = string.IsNullOrWhiteSpace(date) && string.IsNullOrWhiteSpace(time);
        if (!isNow)
        {
            var day = now.Date;
            if (!string.IsNullOrWhiteSpace(date) && !DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                return new JsonResult(new { success = false, message = "That date isn't valid." });
            var clock = now.TimeOfDay;
            if (!string.IsNullOrWhiteSpace(time) && !TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out clock))
                return new JsonResult(new { success = false, message = "That time isn't valid." });
            at = day.Date.Add(clock);
        }

        var duration = await _context.RestaurantSettings.AsNoTracking().Select(x => (int?)x.ReservationDurationMinutes).FirstOrDefaultAsync() ?? 90;
        var windowEnd = at.AddMinutes(duration);

        var tables = await _context.RestaurantTables.AsNoTracking().Where(t => t.DiningAreaId == diningAreaId).ToListAsync();
        var ids = tables.Select(t => t.Id).ToList();

        var dayStart = at.Date;
        var dayEnd = at.Date.AddDays(1);
        var rows = await _context.Reservations.AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.TableId != null && ids.Contains(r.TableId.Value)
                        && r.ReservationDateTime >= dayStart.AddDays(-1) && r.ReservationDateTime < dayEnd
                        && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Seated))
            .ToListAsync();

        string Clock(DateTime d) => d.ToString("HH:mm", CultureInfo.InvariantCulture);
        // Walk-in and phone bookings sit on a shared account; the guest's own name is on the booking.
        string Who(Reservation r) => r.ContactName?.Trim() is { Length: > 0 } cn ? cn : r.Customer?.FullName ?? "A customer";

        var statuses = tables.Select(t =>
        {
            var mine = rows.Where(r => r.TableId == t.Id).OrderBy(r => r.ReservationDateTime).ToList();
            var overlapping = mine.Where(r => r.ReservationDateTime < windowEnd && at < r.ReservationDateTime.AddMinutes(r.DurationMinutes)).ToList();
            var later = mine.Where(r => r.ReservationDateTime >= windowEnd && r.ReservationDateTime < dayEnd).ToList();

            string state, label, detail;
            if (!t.IsActive)
            {
                state = "off"; label = "Off"; detail = "Switched off. Not offered to guests.";
            }
            else if (overlapping.Any(r => r.Status == ReservationStatus.Seated))
            {
                var r = overlapping.First(x => x.Status == ReservationStatus.Seated);
                state = "seated"; label = "Seated"; detail = $"{Who(r)} is seated (party of {r.PartySize}, booked {Clock(r.ReservationDateTime)}).";
            }
            else if (overlapping.Count > 0)
            {
                var r = overlapping[0];
                state = "booked"; label = Clock(r.ReservationDateTime); detail = $"Booked: {Who(r)}, party of {r.PartySize} at {Clock(r.ReservationDateTime)} ({r.Status}).";
            }
            else if (later.Count > 0)
            {
                var r = later[0];
                state = "later"; label = "at " + Clock(r.ReservationDateTime); detail = $"Free now. Next booking: {Who(r)}, party of {r.PartySize} at {Clock(r.ReservationDateTime)}.";
            }
            else
            {
                state = "free"; label = ""; detail = "Free for the rest of the day.";
            }
            return new { tableId = t.Id, state, label, detail };
        }).ToList();

        var tableNumbers = tables.ToDictionary(t => t.Id, t => t.TableNumber);
        var bookings = rows
            .Where(r => r.ReservationDateTime >= dayStart && r.ReservationDateTime < dayEnd)
            .OrderBy(r => r.ReservationDateTime)
            .Select(r => new
            {
                id = r.Id,
                tableId = r.TableId,
                table = tableNumbers.TryGetValue(r.TableId!.Value, out var n) ? n : "?",
                customer = Who(r),
                party = r.PartySize,
                start = Clock(r.ReservationDateTime),
                end = Clock(r.ReservationDateTime.AddMinutes(r.DurationMinutes)),
                status = r.Status.ToString()
            }).ToList();

        return new JsonResult(new
        {
            success = true,
            isNow,
            asOf = at.ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture),
            day = at.ToString("ddd d MMM", CultureInfo.InvariantCulture),
            tables = statuses,
            bookings
        });
    }

    public async Task<IActionResult> OnPostMoveTableAsync(int tableId, int x, int y)
    {
        if (x < 0 || x >= GridCols || y < 0 || y >= GridRows)
            return new JsonResult(new { success = false, message = "That position is outside the floor plan." });

        var table = await _context.RestaurantTables.FindAsync(tableId);
        if (table == null)
            return new JsonResult(new { success = false, message = "Table not found." });

        var occupied = await _context.RestaurantTables.AnyAsync(t =>
            t.Id != tableId && t.DiningAreaId == table.DiningAreaId &&
            t.PositionX == x && t.PositionY == y);
        if (occupied)
            return new JsonResult(new { success = false, message = "Another table is already there." });

        table.PositionX = x;
        table.PositionY = y;
        await _context.SaveChangesAsync();
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostUnplaceTableAsync(int tableId)
    {
        var table = await _context.RestaurantTables.FindAsync(tableId);
        if (table == null)
            return new JsonResult(new { success = false, message = "Table not found." });

        table.PositionX = null;
        table.PositionY = null;
        await _context.SaveChangesAsync();
        return new JsonResult(new { success = true });
    }

    /// The side panel of the plan saves a table as you change it, so this answers JSON with the table as stored.
    public async Task<IActionResult> OnPostUpdateTableAsync(int id, string? tableNumber, int diningAreaId, int capacity, bool isActive)
    {
        var table = await _context.RestaurantTables.FindAsync(id);
        if (table == null)
            return new JsonResult(new { success = false, message = "Table not found." });

        var problem = await ValidateTableAsync(tableNumber, diningAreaId, capacity, id);
        if (problem != null)
            return new JsonResult(new { success = false, message = problem });

        var movedArea = table.DiningAreaId != diningAreaId;
        if (movedArea)
        {
            table.PositionX = null;
            table.PositionY = null;
        }
        table.TableNumber = tableNumber!.Trim();
        table.DiningAreaId = diningAreaId;
        table.Capacity = capacity;
        table.IsActive = isActive;
        await _context.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            movedArea,
            table = new
            {
                id = table.Id,
                tableNumber = table.TableNumber,
                capacity = table.Capacity,
                diningAreaId = table.DiningAreaId,
                positionX = table.PositionX,
                positionY = table.PositionY,
                isActive = table.IsActive
            }
        });
    }
}
