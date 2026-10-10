using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// 
/// Contact details, booking rules, tax, service charge and opening hours. The restaurant's NAME is deliberately not here:
/// it is fixed in the system (see <c>Brand.Name</c>) and shown on every page, email and receipt, so no form on this page can
/// change it, and nothing in the handlers below reads one from the request.
/// 
public class SettingsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;

    public SettingsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService)
    {
        _context = context;
        _userManager = userManager;
        _auditService = auditService;
    }

    public List<OperatingHours> Hours { get; private set; } = new();

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public class ProfileInput
    {
        [StringLength(300, ErrorMessage = "Address: 300 characters at most.")]
        public string? Address { get; set; }

        [StringLength(30, ErrorMessage = "Phone: 30 characters at most.")]
        public string? Phone { get; set; }

        [EmailAddress(ErrorMessage = "Enter a valid contact email."), StringLength(120)]
        public string? Email { get; set; }

        [StringLength(500, ErrorMessage = "Description: 500 characters at most.")]
        public string? Description { get; set; }

        [Range(30, 300, ErrorMessage = "A booking should last between 30 and 300 minutes.")]
        public int ReservationDurationMinutes { get; set; } = 90;

        [Range(1, 365, ErrorMessage = "Guests can book between 1 and 365 days ahead.")]
        public int MaxAdvanceBookingDays { get; set; } = 60;

        [Range(0, 30, ErrorMessage = "Service charge must be between 0 and 30 percent.")]
        public decimal ServiceChargePercent { get; set; } = 10m;

        [Range(0, 30, ErrorMessage = "Tax rate must be between 0 and 30 percent.")]
        public decimal TaxRatePercent { get; set; } = 6m;

        [Range(5, 120, ErrorMessage = "Holding a table can last between 5 and 120 minutes.")]
        public int HoldMinutes { get; set; } = 15;
    }

    public async Task OnGetAsync()
    {
        await LoadAsync();
        var s = await _context.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync() ?? new RestaurantSetting();
        Input = new ProfileInput
        {
            Address = s.Address,
            Phone = s.Phone,
            Email = s.Email,
            Description = s.Description,
            ReservationDurationMinutes = s.ReservationDurationMinutes,
            MaxAdvanceBookingDays = s.MaxAdvanceBookingDays,
            ServiceChargePercent = s.ServiceChargePercent,
            TaxRatePercent = s.TaxRatePercent,
            HoldMinutes = s.HoldMinutes
        };
    }

    private async Task LoadAsync() =>
        Hours = await _context.OperatingHours.AsNoTracking().OrderBy(h => h.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)h.DayOfWeek).ToListAsync();

    public async Task<IActionResult> OnPostSaveProfileAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var setting = await _context.RestaurantSettings.FirstOrDefaultAsync();
        if (setting == null)
        {
            setting = new RestaurantSetting { RestaurantName = Presentation.Brand.Name };
            _context.RestaurantSettings.Add(setting);
        }

        setting.Address = Clean(Input.Address);
        setting.Phone = Clean(Input.Phone);
        setting.Email = Clean(Input.Email);
        setting.Description = Clean(Input.Description);
        setting.ReservationDurationMinutes = Input.ReservationDurationMinutes;
        setting.MaxAdvanceBookingDays = Input.MaxAdvanceBookingDays;
        setting.ServiceChargePercent = Input.ServiceChargePercent;
        setting.TaxRatePercent = Input.TaxRatePercent;
        setting.HoldMinutes = Input.HoldMinutes;
        // The name is never taken from the form. It always follows the system's own value.
        setting.RestaurantName = Presentation.Brand.Name;
        await _context.SaveChangesAsync();

        var acting = await _userManager.GetUserAsync(User);
        if (acting != null)
            await _auditService.LogAsync(acting.Id, acting.FullName, "ChangeSettings", "Restaurant contact details and booking rules updated.");

        TempData["StatusMessage"] = "Settings saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveHoursAsync(List<int> hourId, List<string> openTime, List<string> closeTime, List<int>? closedDayIds)
    {
        closedDayIds ??= new();
        if (hourId.Count != openTime.Count || hourId.Count != closeTime.Count)
        {
            TempData["ErrorMessage"] = "The opening-hours form was incomplete. Please reload the page and try again.";
            return RedirectToPage();
        }

        var allHours = await _context.OperatingHours.ToListAsync();
        var parsed = new List<(OperatingHours Record, TimeSpan Open, TimeSpan Close, bool Closed)>();
        for (var i = 0; i < hourId.Count; i++)
        {
            var record = allHours.FirstOrDefault(h => h.Id == hourId[i]);
            if (record == null) continue;

            var closed = closedDayIds.Contains(hourId[i]);
            if (!TimeSpan.TryParse(openTime[i], out var open) || !TimeSpan.TryParse(closeTime[i], out var close))
            {
                if (closed) { parsed.Add((record, record.OpenTime, record.CloseTime, true)); continue; }
                TempData["ErrorMessage"] = $"{record.DayOfWeek}: please enter both an opening and a closing time, or tick Closed.";
                return RedirectToPage();
            }

            if (!closed && close <= open)
            {
                TempData["ErrorMessage"] = $"{record.DayOfWeek}: closing time must be after opening time.";
                return RedirectToPage();
            }
            parsed.Add((record, open, close, closed));
        }

        foreach (var p in parsed)
        {
            p.Record.IsClosed = p.Closed;
            p.Record.OpenTime = p.Open;
            p.Record.CloseTime = p.Close;
        }
        await _context.SaveChangesAsync();

        var acting = await _userManager.GetUserAsync(User);
        if (acting != null)
            await _auditService.LogAsync(acting.Id, acting.FullName, "ChangeOperatingHours", "Weekly opening hours updated.");

        TempData["StatusMessage"] = "Opening hours saved.";
        return RedirectToPage();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
