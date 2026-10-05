using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

/// <summary>Booked/free status for one table at a specific date/time, used by the
/// admin visual floor plan to overlay live reservation status on each table.</summary>
public record TableStatusInfo(int TableId, bool IsBooked, string? ReservationSummary);

/// <summary>One bookable start time on a day. <see cref="Full"/> means no table fits the party then, so the guest may join the waiting list.</summary>
public record SlotInfo(string Time, bool Full);

/// <summary>What a guest can book on one day: nothing when the restaurant is closed, otherwise the start times in order.</summary>
public record DaySlots(bool Closed, IReadOnlyList<SlotInfo> Slots);

/// <summary>Everything the booking form collects. Staff bookings leave the guest-only fields empty.</summary>
public record ReservationRequest(
    string CustomerId, DateTime Date, TimeSpan Time, int PartySize,
    string? SpecialRequests = null, int? DiningAreaId = null, int? PreferredTableId = null,
    string? Occasion = null, int? SetMenuId = null, string? ContactName = null, string? ContactPhone = null);

/// <summary>A booking or walk-in taken by a member of staff for a guest who has no account (or is on the phone).</summary>
public record StaffBooking(string GuestName, string? Phone, DateTime Date, TimeSpan Time, int PartySize, int? TableId, string? Notes, string? Occasion, bool SeatNow);

public interface IReservationService
{
    /// <summary>Staff: book (or seat right now) a guest who has no account. Filed under the shared walk-in account; the name and number stay on the booking.</summary>
    Task<(bool Success, string Message, Reservation? Reservation)> CreateByStaffAsync(StaffBooking booking, string staffId);

    /// <summary>Staff: move a booking to another table, if it is big enough and free for the whole sitting.</summary>
    Task<(bool Success, string Message)> MoveTableAsync(int reservationId, int tableId);

    /// <summary>Staff: put a cancelled or no-show booking back, on the same table if it is still free, otherwise on another.</summary>
    Task<(bool Success, string Message)> ReopenAsync(int reservationId, string staffId);

    /// <summary>The tables a booking could move to right now: big enough, active and free for its whole sitting (its own table included).</summary>
    Task<List<RestaurantTable>> GetTablesForMoveAsync(int reservationId);

    Task<List<RestaurantTable>> GetAvailableTablesAsync(DateTime date, TimeSpan time, int partySize, int? diningAreaId = null);
    Task<List<TableStatusInfo>> GetTableStatusesAsync(DateTime date, TimeSpan time, int? diningAreaId = null);
    Task<(bool Success, string Message, Reservation? Reservation)> CreateReservationAsync(
        string customerId, DateTime date, TimeSpan time, int partySize, string? specialRequests, int? diningAreaId, int? preferredTableId = null);
    Task<(bool Success, string Message, Reservation? Reservation)> CreateReservationAsync(ReservationRequest request);
    Task<DaySlots> GetSlotsAsync(DateTime date, int partySize, int? diningAreaId = null);
    Task<(bool Success, string Message)> JoinWaitlistAsync(string customerId, DateTime date, TimeSpan time, int partySize);
    Task<(bool Success, string Message)> UpdateReservationAsync(int reservationId, string customerId, DateTime date, TimeSpan time, int partySize, string? specialRequests, bool isStaffOrAdmin);
    Task<(bool Success, string Message)> CancelReservationAsync(int reservationId, string requestingUserId, bool isStaffOrAdmin, string? reason);
    Task<(bool Success, string Message)> ConfirmReservationAsync(int reservationId, string staffId, int? tableId);
    Task<(bool Success, string Message)> UpdateStatusAsync(int reservationId, ReservationStatus status, string staffId);
    Task<List<Reservation>> GetCustomerReservationsAsync(string customerId);
    Task<List<Reservation>> GetReservationsForDateAsync(DateTime date);
    Task<Reservation?> GetByIdAsync(int id);
}

public class ReservationService : IReservationService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;

    public ReservationService(ApplicationDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    ///Slots start every half hour; the last one is this long before closing so the table can still enjoy a full meal.
    public const int SlotStepMinutes = 30;
    public const int LastSeatingBeforeCloseMinutes = 60;

    ///Parties this large are placed on a table but wait for a person on the team to confirm them.
    public const int ConfirmByTeamFromParty = 7;

    private async Task<int> GetDurationMinutesAsync()
    {
        var setting = await _context.RestaurantSettings.FirstOrDefaultAsync();
        return setting?.ReservationDurationMinutes ?? 90;
    }

    public async Task<List<RestaurantTable>> GetAvailableTablesAsync(DateTime date, TimeSpan time, int partySize, int? diningAreaId = null)
    {
        var duration = await GetDurationMinutesAsync();
        var requestedStart = date.Date.Add(time);

        var hours = await _context.OperatingHours.FirstOrDefaultAsync(h => h.DayOfWeek == requestedStart.DayOfWeek);
        if (!IsBookableTime(hours, time))
            return new List<RestaurantTable>();

        var tables = await LoadCandidateTablesAsync(partySize, diningAreaId);
        if (tables.Count == 0)
            return tables;

        var reservations = await LoadActiveReservationsAsync(date, tables.Select(t => t.Id));
        return FreeTables(tables, reservations, requestedStart, duration);
    }

    private static bool IsBookableTime(OperatingHours? hours, TimeSpan time) =>
        hours != null && !hours.IsClosed && time >= hours.OpenTime
        && time <= hours.CloseTime - TimeSpan.FromMinutes(LastSeatingBeforeCloseMinutes);

    private async Task<List<RestaurantTable>> LoadCandidateTablesAsync(int partySize, int? diningAreaId)
    {
        var query = _context.RestaurantTables
            .Include(t => t.DiningArea)
            .Where(t => t.IsActive && t.Capacity >= partySize);
        if (diningAreaId.HasValue)
            query = query.Where(t => t.DiningAreaId == diningAreaId.Value);
        return await query.OrderBy(t => t.Capacity).ThenBy(t => t.TableNumber).ToListAsync();
    }

    // Overlap is filtered in memory: it keeps the SQL simple and avoids TimeSpan arithmetic that EF cannot translate.
    private async Task<List<Reservation>> LoadActiveReservationsAsync(DateTime date, IEnumerable<int> tableIds)
    {
        var ids = tableIds.ToList();
        var dayStart = date.Date.AddDays(-1);
        var dayEnd = date.Date.AddDays(1);
        return await _context.Reservations
            .Where(r => r.TableId != null
                        && ids.Contains(r.TableId!.Value)
                        && r.ReservationDateTime >= dayStart
                        && r.ReservationDateTime < dayEnd
                        && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Seated))
            .ToListAsync();
    }

    private static List<RestaurantTable> FreeTables(List<RestaurantTable> tables, List<Reservation> reservations, DateTime start, int duration, int? ignoreReservationId = null)
    {
        var end = start.AddMinutes(duration);
        var busy = reservations
            .Where(r => r.Id != ignoreReservationId
                        && r.ReservationDateTime < end
                        && start < r.ReservationDateTime.AddMinutes(r.DurationMinutes))
            .Select(r => r.TableId!.Value)
            .ToHashSet();
        return tables.Where(t => !busy.Contains(t.Id)).ToList();
    }

    public async Task<DaySlots> GetSlotsAsync(DateTime date, int partySize, int? diningAreaId = null)
    {
        var hours = await _context.OperatingHours.FirstOrDefaultAsync(h => h.DayOfWeek == date.DayOfWeek);
        if (hours == null || hours.IsClosed)
            return new DaySlots(true, Array.Empty<SlotInfo>());

        var duration = await GetDurationMinutesAsync();
        var tables = await LoadCandidateTablesAsync(partySize, diningAreaId);
        var reservations = tables.Count == 0 ? new List<Reservation>() : await LoadActiveReservationsAsync(date, tables.Select(t => t.Id));

        var last = hours.CloseTime - TimeSpan.FromMinutes(LastSeatingBeforeCloseMinutes);
        var slots = new List<SlotInfo>();
        for (var t = hours.OpenTime; t <= last; t = t.Add(TimeSpan.FromMinutes(SlotStepMinutes)))
        {
            var start = date.Date.Add(t);
            if (start <= DateTime.Now) continue; // today's slots that have gone by are not offered
            var full = FreeTables(tables, reservations, start, duration).Count == 0;
            slots.Add(new SlotInfo(t.ToString(@"hh\:mm"), full));
        }
        return new DaySlots(false, slots);
    }

    public async Task<(bool Success, string Message)> JoinWaitlistAsync(string customerId, DateTime date, TimeSpan time, int partySize)
    {
        var start = date.Date.Add(time);
        if (start <= DateTime.Now)
            return (false, "That time has already gone by.");
        if (partySize < 1 || partySize > 30)
            return (false, "Party size must be between 1 and 30.");

        var free = await GetAvailableTablesAsync(date, time, partySize);
        if (free.Count > 0)
            return (false, "A table has just come free at that time. Please book it now.");

        var already = await _context.WaitlistEntries.AnyAsync(w =>
            w.CustomerId == customerId && w.WantedDateTime == start && w.NotifiedAt == null);
        if (already)
            return (true, "You are already on the waiting list for that time.");

        _context.WaitlistEntries.Add(new WaitlistEntry { CustomerId = customerId, WantedDateTime = start, PartySize = partySize });
        await _context.SaveChangesAsync();
        return (true, $"You are on the waiting list for {start:dddd d MMMM} at {start:HH:mm}. We will message you if a table opens.");
    }

    ///After a booking is cancelled or moved, tell the people waiting for that time (oldest first, one per table that is now free).
    private async Task NotifyWaitlistAsync(DateTime start)
    {
        var waiting = await _context.WaitlistEntries
            .Where(w => w.WantedDateTime == start && w.NotifiedAt == null)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync();
        if (waiting.Count == 0) return;

        foreach (var entry in waiting)
        {
            var free = await GetAvailableTablesAsync(start.Date, start.TimeOfDay, entry.PartySize);
            if (free.Count == 0) break;
            entry.NotifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _notificationService.NotifyAsync(
                entry.CustomerId,
                "A table has opened",
                $"A table for {entry.PartySize} is free on {start:dddd d MMMM} at {start:HH:mm}. It is yours if you book soon.",
                NotificationType.Reservation,
                "/Reservations/Create");
        }
    }

    public async Task<List<TableStatusInfo>> GetTableStatusesAsync(DateTime date, TimeSpan time, int? diningAreaId = null)
    {
        var duration = await GetDurationMinutesAsync();
        var requestedStart = date.Date.Add(time);
        var requestedEnd = requestedStart.AddMinutes(duration);

        var tablesQuery = _context.RestaurantTables.Where(t => t.IsActive);
        if (diningAreaId.HasValue)
            tablesQuery = tablesQuery.Where(t => t.DiningAreaId == diningAreaId.Value);
        var tables = await tablesQuery.ToListAsync();
        if (tables.Count == 0)
            return new List<TableStatusInfo>();

        var tableIds = tables.Select(t => t.Id).ToList();
        var dayStart = date.Date.AddDays(-1);
        var dayEnd = date.Date.AddDays(1);

        var sameWindowReservations = await _context.Reservations
            .Include(r => r.Customer)
            .Where(r => r.TableId != null
                        && tableIds.Contains(r.TableId!.Value)
                        && r.ReservationDateTime >= dayStart
                        && r.ReservationDateTime < dayEnd
                        && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Seated))
            .ToListAsync();

        var overlapping = sameWindowReservations
            .Where(r => r.ReservationDateTime < requestedEnd && requestedStart < r.ReservationDateTime.AddMinutes(r.DurationMinutes))
            .ToList();

        var byTable = overlapping
            .GroupBy(r => r.TableId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        return tables.Select(t =>
        {
            byTable.TryGetValue(t.Id, out var reservation);
            var summary = reservation == null ? null :
                $"{reservation.Customer?.FullName ?? "A customer"} - party of {reservation.PartySize} at {reservation.ReservationDateTime:HH:mm}";
            return new TableStatusInfo(t.Id, reservation != null, summary);
        }).ToList();
    }

    public Task<(bool Success, string Message, Reservation? Reservation)> CreateReservationAsync(
        string customerId, DateTime date, TimeSpan time, int partySize, string? specialRequests, int? diningAreaId, int? preferredTableId = null)
        => CreateReservationAsync(new ReservationRequest(customerId, date, time, partySize, specialRequests, diningAreaId, preferredTableId));

    public async Task<(bool Success, string Message, Reservation? Reservation)> CreateReservationAsync(ReservationRequest request)
    {
        var setting = await _context.RestaurantSettings.FirstOrDefaultAsync();
        var duration = setting?.ReservationDurationMinutes ?? 90;
        var maxAdvanceDays = setting?.MaxAdvanceBookingDays ?? 60;

        var requestedStart = request.Date.Date.Add(request.Time);
        if (requestedStart < DateTime.Now.AddMinutes(-1))
            return (false, "You can't book a table in the past.", null);
        if (requestedStart > DateTime.Now.AddDays(maxAdvanceDays))
            return (false, $"Reservations can only be made up to {maxAdvanceDays} days in advance.", null);
        if (request.PartySize < 1 || request.PartySize > 30)
            return (false, "Party size must be between 1 and 30.", null);

        if (request.SetMenuId.HasValue &&
            !await _context.SetMenus.AnyAsync(m => m.Id == request.SetMenuId.Value && m.Status == SetMenuStatus.Active))
            return (false, "That set menu is not on offer any more. Please choose another, or leave it for the night.", null);

        var availableTables = await GetAvailableTablesAsync(request.Date, request.Time, request.PartySize, request.DiningAreaId);
        if (availableTables.Count == 0)
            return (false, "Sorry, there is no table free at that time. Please pick another time, or join the waiting list.", null);

        var chosenTable = request.PreferredTableId.HasValue
            ? availableTables.FirstOrDefault(t => t.Id == request.PreferredTableId.Value)
            : availableTables.First(); // smallest table that fits: the list is ordered by capacity

        if (chosenTable == null)
            return (false, "The table you selected is no longer available. Please pick another one.", null);

        var needsTeam = request.PartySize >= ConfirmByTeamFromParty;
        var reservation = new Reservation
        {
            CustomerId = request.CustomerId,
            TableId = chosenTable.Id,
            Table = chosenTable,
            PartySize = request.PartySize,
            ReservationDateTime = requestedStart,
            DurationMinutes = duration,
            Status = needsTeam ? ReservationStatus.Pending : ReservationStatus.Confirmed,
            SpecialRequests = Trim(request.SpecialRequests, 400),
            Occasion = Trim(request.Occasion, 40),
            SetMenuId = request.SetMenuId,
            ContactName = Trim(request.ContactName, 80),
            ContactPhone = Trim(request.ContactPhone, 30),
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = needsTeam ? null : DateTime.UtcNow
        };

        _context.Reservations.Add(reservation);
        await _context.SaveChangesAsync();

    }
}