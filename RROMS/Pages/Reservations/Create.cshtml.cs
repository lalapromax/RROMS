using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Reservations;

public class CreateModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(IReservationService reservationService, ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _reservationService = reservationService;
        _context = context;
        _userManager = userManager;
    }

    [BindProperty]
    public ReservationFormVM Form { get; set; } = new();

    public List<DiningArea> DiningAreas { get; set; } = new();
    public List<RestaurantTable> AvailableTables { get; set; } = new();
    public bool HasSearched { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDiningAreasAsync();
    }

    public async Task<IActionResult> OnPostCheckAvailabilityAsync()
    {
        await LoadDiningAreasAsync();
        HasSearched = true;

        if (!TimeSpan.TryParse(Form.ReservationTime, out var time))
        {
            ModelState.AddModelError("Form.ReservationTime", "Please choose a valid time.");
            return Page();
        }

        AvailableTables = await _reservationService.GetAvailableTablesAsync(Form.ReservationDate, time, Form.PartySize, Form.DiningAreaId);
        if (AvailableTables.Count == 0)
            ModelState.AddModelError(string.Empty, "No tables are available for that date, time, and party size. Try a different time, or a different party size.");

        return Page();
    }

    public async Task<IActionResult> OnPostBookAsync()
    {
        await LoadDiningAreasAsync();
        HasSearched = true;

        if (!TimeSpan.TryParse(Form.ReservationTime, out var time))
        {
            ModelState.AddModelError("Form.ReservationTime", "Please choose a valid time.");
            return Page();
        }

        var userId = _userManager.GetUserId(User)!;
        var (success, message, reservation) = await _reservationService.CreateReservationAsync(
            userId, Form.ReservationDate, time, Form.PartySize, Form.SpecialRequests, Form.DiningAreaId, Form.SelectedTableId);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            AvailableTables = await _reservationService.GetAvailableTablesAsync(Form.ReservationDate, time, Form.PartySize, Form.DiningAreaId);
            return Page();
        }

        TempData["StatusMessage"] = $"Reservation confirmed for {reservation!.ReservationDateTime:dddd, dd MMM yyyy 'at' h:mm tt} - Table {reservation.Table?.TableNumber}.";
        return RedirectToPage("/Reservations/Index");
    }

    private async Task LoadDiningAreasAsync()
    {
        DiningAreas = await _context.DiningAreas.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync();
    }
}
