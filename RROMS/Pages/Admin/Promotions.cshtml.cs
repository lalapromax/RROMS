using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// <summary>
/// Admin > Promotions: one list, and one dialog for adding or editing. Whether a promotion may be used is decided at checkout
/// by <see cref="IPromotionService"/>; this page only configures it, and every value is validated by <see cref="PromotionValidation"/>.
/// </summary>
public class PromotionsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;

    public PromotionsModel(ApplicationDbContext context, INotificationService notificationService, UserManager<ApplicationUser> userManager, IAuditService audit)
    {
        _context = context;
        _notificationService = notificationService;
        _userManager = userManager;
        _audit = audit;
    }

    public List<Promotion> Promotions { get; set; } = new();
    public List<MenuItem> MenuItems { get; set; } = new();
    public List<MenuCategory> MenuCategories { get; set; } = new();
    /// <summary>How many orders have used each promotion.</summary>
    public Dictionary<int, int> Uses { get; set; } = new();

    [BindProperty] public PromotionForm Input { get; set; } = new();
    /// <summary>True when the dialog should open again because a save was refused; <see cref="DialogError"/> says why.</summary>
    public bool OpenDialog { get; set; }
    public string? DialogError { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Promotions = await _context.Promotions
            .Include(p => p.MenuItem)
            .Include(p => p.MenuCategory)
            .OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id)
            .ToListAsync();
        MenuItems = await _context.MenuItems.Where(m => m.MenuCategory != null && !m.MenuCategory.SetMenuOnly).OrderBy(m => m.Name).ToListAsync();
        MenuCategories = await _context.MenuCategories.Where(c => !c.SetMenuOnly).OrderBy(c => c.DisplayOrder).ToListAsync();
        Uses = await _context.PromotionRedemptions
            .GroupBy(r => r.PromotionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var check = await PromotionValidation.ValidateAsync(_context, Input);
        if (!check.Valid)
        {
            // Show the same dialog again with what was typed and the reason, instead of throwing the form away.
            await LoadAsync();
            OpenDialog = true;
            DialogError = check.Error;
            return Page();
        }

        Promotion promo;
        var isNew = Input.Id == 0;
        if (isNew)
        {
            promo = new Promotion();
            _context.Promotions.Add(promo);
        }
        else
        {
            var existing = await _context.Promotions.FirstOrDefaultAsync(p => p.Id == Input.Id);
            if (existing == null)
            {
                TempData["ErrorMessage"] = "That promotion no longer exists.";
                return RedirectToPage();
            }
            promo = existing;
        }

        promo.Title = check.Title;
        promo.Description = check.Description;
        promo.DiscountPercent = check.DiscountPercent;
        promo.FixedAmountOff = check.FixedAmountOff;
        promo.Code = check.Code;
        promo.MenuItemId = check.MenuItemId;
        promo.MenuCategoryId = check.MenuCategoryId;
        promo.StartDate = check.StartDate;
        promo.EndDate = check.EndDate;
        promo.IsActive = Input.IsActive;
        promo.AllowedDaysMask = check.AllowedDaysMask;
        promo.EligibleEmailsRaw = check.EligibleEmails;
        promo.MinimumOrderAmount = Input.MinimumOrderAmount is > 0 ? Input.MinimumOrderAmount : null;
        promo.MaxRedemptionsTotal = Input.MaxRedemptionsTotal;
        promo.MaxRedemptionsPerCustomer = Input.MaxRedemptionsPerCustomer;
        await _context.SaveChangesAsync();

        // Telling every guest only makes sense for an open promotion: a code is meant to stay with whoever was given it,
        // and a promotion for named guests is not for everyone.
        var told = false;
        if (isNew && Input.NotifyCustomers && promo.IsActive && promo.Code == null && !promo.IsEligibilityRestricted)
        {
            await _notificationService.NotifyAllCustomersAsync(
                "New promotion: " + promo.Title,
                promo.Description ?? $"{promo.DiscountPercent:0.##}% off, running {promo.StartDate:d MMM} to {promo.EndDate:d MMM}.",
                NotificationType.Promotion,
                "/Menu/Index");
            told = true;
        }

        await LogAsync(isNew ? "AddPromotion" : "EditPromotion", promo.Title);
        TempData["StatusMessage"] = isNew ? (told ? "Promotion added and guests told." : "Promotion added.") : "Promotion saved.";
        return RedirectToPage();
    }

    /// <summary>Stops a promotion today. It stays in the list as Ended, with its history.</summary>
    public async Task<IActionResult> OnPostEndAsync(int id)
    {
        var promo = await _context.Promotions.FindAsync(id);
        if (promo == null) return RedirectToPage();

        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        promo.EndDate = yesterday;
        if (promo.StartDate > yesterday) promo.StartDate = yesterday;
        await _context.SaveChangesAsync();
        await LogAsync("EndPromotion", promo.Title);
        TempData["StatusMessage"] = $"{promo.Title} has ended.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var promo = await _context.Promotions.FindAsync(id);
        if (promo == null) return RedirectToPage();

        var hasRedemptions = await _context.PromotionRedemptions.AnyAsync(r => r.PromotionId == id);
        if (hasRedemptions)
        {
            // Keep the redemption history (and every order that used it) intact: switch it off instead of deleting it,
            // the same way a dish that has been ordered before is switched off.
            promo.IsActive = false;
            await _context.SaveChangesAsync();
            await LogAsync("SwitchOffPromotion", promo.Title);
            TempData["StatusMessage"] = "Guests have already used this promotion, so it was switched off and kept for the records instead of deleted.";
            return RedirectToPage();
        }

        _context.Promotions.Remove(promo);
        await _context.SaveChangesAsync();
        await LogAsync("DeletePromotion", promo.Title);
        TempData["StatusMessage"] = "Promotion removed.";
        return RedirectToPage();
    }

    private async Task LogAsync(string action, string details)
    {
        var acting = await _userManager.GetUserAsync(User);
        if (acting != null) await _audit.LogAsync(acting.Id, acting.FullName, action, details);
    }
}
