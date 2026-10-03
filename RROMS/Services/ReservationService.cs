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

    /// <summary>Slots start every half hour; the last one is this long before closing so the table can still enjoy a full meal.</summary>
    public const int SlotStepMinutes = 30;
    public const int LastSeatingBeforeCloseMinutes = 60;

    /// <summary>Parties this large are placed on a table but wait for a person on the team to confirm them.</summary>
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

    /// <summary>After a booking is cancelled or moved, tell the people waiting for that time (oldest first, one per table that is now free).</summary>
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

    /// <summary>
    /// Like GetAvailableTablesAsync's overlap check, but for EVERY active table
    /// in the dining area (not filtered by party size) and reporting who holds
    /// the booking - this is what drives the colour-coded status on the visual
    /// floor plan, not a capacity-eligibility check.
    /// </summary>
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

        await _notificationService.NotifyAsync(
            request.CustomerId,
            needsTeam ? "Reservation received" : "Reservation confirmed",
            needsTeam
                ? $"We have your request for {request.PartySize} on {requestedStart:dddd d MMMM} at {requestedStart:HH:mm}. The team will confirm it within the day."
                : $"Your table for {request.PartySize} on {requestedStart:dddd d MMMM} at {requestedStart:HH:mm} is confirmed.",
            NotificationType.Reservation,
            "/Reservations/Index");

        return (true, needsTeam ? "Reservation received." : "Reservation confirmed.", reservation);
    }

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length > max ? v[..max] : v;
    }

    public async Task<(bool Success, string Message)> UpdateReservationAsync(
        int reservationId, string customerId, DateTime date, TimeSpan time, int partySize, string? specialRequests, bool isStaffOrAdmin)
    {
        var reservation = await _context.Reservations.Include(r => r.Table).FirstOrDefaultAsync(r => r.Id == reservationId);
        if (reservation == null)
            return (false, "Reservation not found.");
        if (!isStaffOrAdmin && reservation.CustomerId != customerId)
            return (false, "You can only modify your own reservations.");
        if (reservation.Status is ReservationStatus.Completed or ReservationStatus.Cancelled or ReservationStatus.NoShow or ReservationStatus.Seated)
            return (false, "This reservation can no longer be modified.");
        if (!isStaffOrAdmin && !reservation.GuestCanChange)
            return (false, $"It is less than {Reservation.GuestChangeCutoffHours} hours to your booking, so please call us and we will do what we can.");
        if (partySize < 1 || partySize > 30)
            return (false, "Party size must be between 1 and 30.");

        var newStart = date.Date.Add(time);
        if (newStart < DateTime.Now)
            return (false, "That time has already gone by.");

        var hours = await _context.OperatingHours.FirstOrDefaultAsync(h => h.DayOfWeek == newStart.DayOfWeek);
        if (!IsBookableTime(hours, time))
            return (false, "We are not taking bookings at that time. Please choose another.");

        var setting = await _context.RestaurantSettings.FirstOrDefaultAsync();
        var duration = setting?.ReservationDurationMinutes ?? 90;

        // The booking's own table counts as free for itself, so keeping the same table is possible.
        var tables = await LoadCandidateTablesAsync(partySize, null);
        var reservations = tables.Count == 0 ? new List<Reservation>() : await LoadActiveReservationsAsync(date, tables.Select(t => t.Id));
        var free = FreeTables(tables, reservations, newStart, duration, ignoreReservationId: reservation.Id);

        var chosen = free.FirstOrDefault(t => t.Id == reservation.TableId) ?? free.FirstOrDefault();
        if (chosen == null)
            return (false, "No table is free for that time and party size. Please try another time.");

        var oldStart = reservation.ReservationDateTime;
        reservation.ReservationDateTime = newStart;
        reservation.DurationMinutes = duration;
        reservation.PartySize = partySize;
        reservation.SpecialRequests = Trim(specialRequests, 400);
        reservation.TableId = chosen.Id;
        reservation.Status = partySize >= ConfirmByTeamFromParty && !isStaffOrAdmin ? ReservationStatus.Pending : ReservationStatus.Confirmed;
        if (reservation.Status == ReservationStatus.Confirmed) reservation.ConfirmedAt ??= DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            reservation.CustomerId,
            "Reservation updated",
            $"Your reservation is now for {newStart:dddd d MMMM} at {newStart:HH:mm}, {partySize} {(partySize == 1 ? "guest" : "guests")}.",
            NotificationType.Reservation,
            "/Reservations/Index");

        if (oldStart != newStart) await NotifyWaitlistAsync(oldStart);
        return (true, "Reservation updated.");
    }

    public async Task<(bool Success, string Message)> CancelReservationAsync(int reservationId, string requestingUserId, bool isStaffOrAdmin, string? reason)
    {
        var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId);
        if (reservation == null)
            return (false, "Reservation not found.");
        if (!isStaffOrAdmin && reservation.CustomerId != requestingUserId)
            return (false, "You can only cancel your own reservations.");
        if (reservation.Status is ReservationStatus.Completed or ReservationStatus.Cancelled)
            return (false, "This reservation is already closed.");
        if (!isStaffOrAdmin && !reservation.GuestCanChange)
            return (false, $"It is less than {Reservation.GuestChangeCutoffHours} hours to your booking, so please call us and we will do what we can.");

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancellationReason = reason;
        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            reservation.CustomerId,
            "Reservation cancelled",
            $"Your reservation for {reservation.ReservationDateTime:dddd d MMMM} at {reservation.ReservationDateTime:HH:mm} has been cancelled."
                + (isStaffOrAdmin && !string.IsNullOrWhiteSpace(reason) && reason != "Cancelled by staff" ? " " + reason.Trim() : ""),
            NotificationType.Reservation,
            "/Reservations/Index");

        await NotifyWaitlistAsync(reservation.ReservationDateTime);
        return (true, "Reservation cancelled.");
    }

    public async Task<(bool Success, string Message)> ConfirmReservationAsync(int reservationId, string staffId, int? tableId)
    {
        var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId);
        if (reservation == null)
            return (false, "Reservation not found.");

        if (tableId.HasValue)
            reservation.TableId = tableId.Value;

        reservation.Status = ReservationStatus.Confirmed;
        reservation.ConfirmedAt = DateTime.UtcNow;
        reservation.ConfirmedByStaffId = staffId;
        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(
            reservation.CustomerId,
            "Reservation confirmed",
            $"Our team has confirmed your reservation for {reservation.ReservationDateTime:dddd, dd MMM yyyy} at {reservation.ReservationDateTime:HH:mm}.",
            NotificationType.Reservation,
            "/Reservations/Index");

        return (true, "Reservation confirmed.");
    }

    public async Task<(bool Success, string Message, Reservation? Reservation)> CreateByStaffAsync(StaffBooking b, string staffId)
    {
        var name = b.GuestName?.Trim() ?? string.Empty;
        if (name.Length == 0) return (false, "Please give the guest's name.", null);
        if (name.Length > 80) return (false, "The name is a little long. Eighty characters at most.", null);
        if (b.PartySize < 1 || b.PartySize > 30) return (false, "Party size must be between 1 and 30.", null);

        var walkIn = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == ApplicationUser.WalkInEmail);
        if (walkIn == null) return (false, "The walk-in account is missing. Restart the site once so it is created.", null);

        var setting = await _context.RestaurantSettings.FirstOrDefaultAsync();
        var duration = setting?.ReservationDurationMinutes ?? 90;

        DateTime start;
        if (b.SeatNow)
        {
            var now = DateTime.Now;
            start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute - now.Minute % 5, 0);
        }
        else
        {
            start = b.Date.Date.Add(b.Time);
            if (start < DateTime.Now.AddMinutes(-15)) return (false, "That time has already gone by.", null);
            var hours = await _context.OperatingHours.FirstOrDefaultAsync(h => h.DayOfWeek == start.DayOfWeek);
            if (!IsBookableTime(hours, b.Time)) return (false, "We are not seating at that time. Please choose another.", null);
        }

        var tables = await LoadCandidateTablesAsync(b.PartySize, null);
        var reservations = tables.Count == 0 ? new List<Reservation>() : await LoadActiveReservationsAsync(start, tables.Select(t => t.Id));
        var free = FreeTables(tables, reservations, start, duration);
        var chosen = b.TableId.HasValue ? free.FirstOrDefault(t => t.Id == b.TableId.Value) : free.FirstOrDefault();
        if (chosen == null)
            return (false, b.TableId.HasValue ? "That table is not free for this sitting. Choose another, or leave it to us." : "No table is free for that time and party size.", null);

        var phone = Trim(b.Phone, 30);
        var reservation = new Reservation
        {
            CustomerId = walkIn.Id,
            TableId = chosen.Id,
            Table = chosen,
            PartySize = b.PartySize,
            ReservationDateTime = start,
            DurationMinutes = duration,
            Status = b.SeatNow ? ReservationStatus.Seated : ReservationStatus.Confirmed,
            SpecialRequests = Trim(b.Notes, 400),
            Occasion = Trim(b.Occasion, 40),
            ContactName = name,
            ContactPhone = phone,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.UtcNow,
            ConfirmedByStaffId = staffId
        };
        _context.Reservations.Add(reservation);
        await _context.SaveChangesAsync();
        return (true, b.SeatNow ? $"{name} is seated at {chosen.TableNumber}." : $"Booked {name} at {chosen.TableNumber}.", reservation);
    }

    public async Task<List<RestaurantTable>> GetTablesForMoveAsync(int reservationId)
    {
        var r = await _context.Reservations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reservationId);
        if (r == null) return new List<RestaurantTable>();
        var tables = await LoadCandidateTablesAsync(r.PartySize, null);
        if (tables.Count == 0) return tables;
        var reservations = await LoadActiveReservationsAsync(r.ReservationDateTime, tables.Select(t => t.Id));
        return FreeTables(tables, reservations, r.ReservationDateTime, r.DurationMinutes, ignoreReservationId: r.Id);
    }

    public async Task<(bool Success, string Message)> MoveTableAsync(int reservationId, int tableId)
    {
        var r = await _context.Reservations.FirstOrDefaultAsync(x => x.Id == reservationId);
        if (r == null) return (false, "Reservation not found.");
        if (r.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated))
            return (false, "This booking is closed, so its table cannot be changed.");
        if (r.TableId == tableId) return (true, "Already at that table.");

        var free = await GetTablesForMoveAsync(reservationId);
        var table = free.FirstOrDefault(t => t.Id == tableId);
        if (table == null) return (false, "That table is too small, or taken for this sitting.");

        r.TableId = table.Id;
        await _context.SaveChangesAsync();
        return (true, $"Moved to {table.TableNumber}.");
    }

    public async Task<(bool Success, string Message)> ReopenAsync(int reservationId, string staffId)
    {
        var r = await _context.Reservations.FirstOrDefaultAsync(x => x.Id == reservationId);
        if (r == null) return (false, "Reservation not found.");
        if (r.Status is not (ReservationStatus.Cancelled or ReservationStatus.NoShow))
            return (false, "Only a cancelled or no-show booking can be reopened.");
        if (r.ReservationEndDateTime < DateTime.Now)
            return (false, "That sitting is over, so it cannot be reopened. Make a new booking instead.");

        var tables = await LoadCandidateTablesAsync(r.PartySize, null);
        var reservations = tables.Count == 0 ? new List<Reservation>() : await LoadActiveReservationsAsync(r.ReservationDateTime, tables.Select(t => t.Id));
        var free = FreeTables(tables, reservations, r.ReservationDateTime, r.DurationMinutes, ignoreReservationId: r.Id);
        var chosen = free.FirstOrDefault(t => t.Id == r.TableId) ?? free.FirstOrDefault();
        if (chosen == null) return (false, "No table is free for that time any more, so it cannot be reopened.");

        r.TableId = chosen.Id;
        r.Status = ReservationStatus.Confirmed;
        r.CancellationReason = null;
        r.ConfirmedAt = DateTime.UtcNow;
        r.ConfirmedByStaffId = staffId;
        await _context.SaveChangesAsync();

        await _notificationService.NotifyAsync(r.CustomerId, "Reservation back on",
            $"Your table for {r.ReservationDateTime:dddd d MMMM} at {r.ReservationDateTime:HH:mm} is booked again.",
            NotificationType.Reservation, "/Reservations/Index");
        return (true, $"Reopened on {chosen.TableNumber}.");
    }

    public async Task<(bool Success, string Message)> UpdateStatusAsync(int reservationId, ReservationStatus status, string staffId)
    {
        var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId);
        if (reservation == null)
            return (false, "Reservation not found.");

        reservation.Status = status;
        if (status == ReservationStatus.Confirmed)
        {
            reservation.ConfirmedAt = DateTime.UtcNow;
            reservation.ConfirmedByStaffId = staffId;
        }
        await _context.SaveChangesAsync();

        // Seating a guest and closing the sitting are the team's own bookkeeping: the guest is already at the table.
        if (status is ReservationStatus.Seated or ReservationStatus.Completed)
            return (true, status == ReservationStatus.Seated ? "Seated." : "Finished.");

        await _notificationService.NotifyAsync(
            reservation.CustomerId,
            "Reservation status updated",
            $"Your reservation is now marked as {status}.",
            NotificationType.Reservation,
            "/Reservations/Index");

        return (true, "Status updated.");
    }

    public async Task<List<Reservation>> GetCustomerReservationsAsync(string customerId)
    {
        return await _context.Reservations
            .Include(r => r.Table).ThenInclude(t => t!.DiningArea)
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.ReservationDateTime)
            .ToListAsync();
    }

    public async Task<List<Reservation>> GetReservationsForDateAsync(DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        return await _context.Reservations
            .Include(r => r.Customer)
            .Include(r => r.Table).ThenInclude(t => t!.DiningArea)
            .Include(r => r.Orders) // so staff can see which order(s) go with this reservation (brief 5.D)
            .Include(r => r.SetMenu)
            .Where(r => r.ReservationDateTime >= start && r.ReservationDateTime < end)
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();
    }

    public async Task<Reservation?> GetByIdAsync(int id)
    {
        return await _context.Reservations
            .Include(r => r.Table).ThenInclude(t => t!.DiningArea)
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == id);
    }
}
