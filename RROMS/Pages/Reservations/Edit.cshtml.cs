using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Reservations;

public class EditModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(IReservationService reservationService, UserManager<ApplicationUser> userManager)
    {
        _reservationService = reservationService;
        _userManager = userManager;
    }

    [BindProperty]
    public ReservationFormVM Form { get; set; } = new();

    public List<RestaurantTable> AvailableTables { get; set; } = new();
    public bool HasSearched { get; set; }
    public Reservation? Reservation { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var reservation = await _reservationService.GetByIdAsync(id);
        if (reservation == null || reservation.CustomerId != userId)
            return NotFound();
        if (reservation.Status is ReservationStatus.Completed or ReservationStatus.Cancelled or ReservationStatus.NoShow)
        {
            TempData["ErrorMessage"] = "This reservation can no longer be modified.";
            return RedirectToPage("/Reservations/Index");
        }

        Reservation = reservation;
        Form = new ReservationFormVM
        {
            ReservationId = reservation.Id,
            ReservationDate = reservation.ReservationDateTime.Date,
            ReservationTime = reservation.ReservationDateTime.ToString("HH:mm"),
            PartySize = reservation.PartySize,
            SpecialRequests = reservation.SpecialRequests,
            DiningAreaId = reservation.Table?.DiningAreaId
        };
        return Page();
    }

    public async Task<IActionResult> OnPostCheckAvailabilityAsync(int id)
    {
        await LoadReservationAsync(id);
        HasSearched = true;

        if (!TimeSpan.TryParse(Form.ReservationTime, out var time))
        {
            ModelState.AddModelError("Form.ReservationTime", "Please choose a valid time.");
            return Page();
        }

        AvailableTables = await _reservationService.GetAvailableTablesAsync(Form.ReservationDate, time, Form.PartySize, Form.DiningAreaId);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id)
    {
        await LoadReservationAsync(id);

        if (!TimeSpan.TryParse(Form.ReservationTime, out var time))
        {
            ModelState.AddModelError("Form.ReservationTime", "Please choose a valid time.");
            return Page();
        }

        var userId = _userManager.GetUserId(User)!;
        var (success, message) = await _reservationService.UpdateReservationAsync(
            id, userId, Form.ReservationDate, time, Form.PartySize, Form.SpecialRequests, isStaffOrAdmin: false);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            HasSearched = true;
            AvailableTables = await _reservationService.GetAvailableTablesAsync(Form.ReservationDate, time, Form.PartySize, Form.DiningAreaId);
            return Page();
        }

        TempData["StatusMessage"] = message;
        return RedirectToPage("/Reservations/Index");
    }

    private async Task LoadReservationAsync(int id)
    {
        Reservation = await _reservationService.GetByIdAsync(id);
    }
}
