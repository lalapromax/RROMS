using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Reservations;

public class IndexModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IReservationService reservationService, UserManager<ApplicationUser> userManager)
    {
        _reservationService = reservationService;
        _userManager = userManager;
    }

    public List<Reservation> Upcoming { get; set; } = new();
    public List<Reservation> Past { get; set; } = new();

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User)!;
        var all = await _reservationService.GetCustomerReservationsAsync(userId);
        Upcoming = all.Where(r => r.ReservationDateTime >= DateTime.Now
                                   && r.Status != ReservationStatus.Cancelled
                                   && r.Status != ReservationStatus.Completed
                                   && r.Status != ReservationStatus.NoShow)
                      .OrderBy(r => r.ReservationDateTime)
                      .ToList();
        Past = all.Except(Upcoming).OrderByDescending(r => r.ReservationDateTime).ToList();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var (success, message) = await _reservationService.CancelReservationAsync(id, userId, isStaffOrAdmin: false, reason: "Cancelled by customer");
        TempData[success ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage();
    }
}
