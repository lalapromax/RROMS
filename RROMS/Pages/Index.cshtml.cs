using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IRecommendationService _recommendations;
    private readonly IPromotionService _promotions;
    private readonly ISetMenuService _setMenus;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext context, IRecommendationService recommendations, IPromotionService promotions, ISetMenuService setMenus, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _recommendations = recommendations;
        _promotions = promotions;
        _setMenus = setMenus;
        _userManager = userManager;
    }

    /// Set menus on offer today (up to three), the first of which is shown as the printed menu card.
    public List<SetMenu> Sets { get; private set; } = new();

    /// "On the pass this week": the dishes guests order most, one large and three small.
    public List<MenuItem> Pass { get; private set; } = new();
    public Dictionary<int, decimal> Discounts { get; private set; } = new();

    /// Four wines for the cellar teaser.
    public List<MenuItem> Wines { get; private set; } = new();

    public List<RewardTier> Tiers { get; private set; } = new();

    /// Published partners for the quiet strip further down. Hidden when there are none.
    public List<Collaboration> Collaborations { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        // The guest home page is for visitors and customers. Staff and administrators land on their own areas.
        if (User.IsInRole(Roles.Administrator)) return RedirectToPage("/Admin/Dashboard");
        if (User.IsInRole(Roles.Staff)) return RedirectToPage("/Staff/Dashboard");

        Sets = (await _setMenus.ListForGuestsAsync()).Take(3).ToList();

        DietaryProfile? viewer = null;
        if (User.IsInRole(Roles.Customer))
        {
            var me = await _userManager.GetUserAsync(User);
            if (me != null) viewer = DietaryRules.ProfileFor(me);
        }

        var popular = await _recommendations.GetPopularItemsAsync(40);
        var food = popular.Where(m => m.MenuCategory is { IsWine: false, SetMenuOnly: false }).ToList();
        if (viewer?.HasAnyRestriction == true) food = food.Where(m => DietaryRules.IsSuitable(m, viewer)).ToList();
        Pass = food.OrderByDescending(m => m.IsChefsPick).Take(4).ToList();

        var viewerId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;
        var viewerEmail = viewerId != null ? await _context.Users.Where(u => u.Id == viewerId).Select(u => u.Email).FirstOrDefaultAsync() : null;
        Discounts = await _promotions.GetDisplayDiscountsAsync(Pass, viewerEmail);

        Wines = await _context.MenuItems.AsNoTracking().Include(m => m.MenuCategory)
            .Where(m => m.IsAvailable && m.MenuCategory!.IsWine && m.MenuCategory.IsActive)
            .OrderBy(m => m.MenuCategory!.DisplayOrder).ThenByDescending(m => m.Price).Take(4).ToListAsync();

        Tiers = await _context.RewardTiers.AsNoTracking().Include(t => t.Perks).OrderBy(t => t.MinPoints).ToListAsync();

        Collaborations = await _context.Collaborations.AsNoTracking()
            .Where(c => c.IsPublished).OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id).Take(24).ToListAsync();

        return Page();
    }
}
