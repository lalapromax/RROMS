using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Menu;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IRecommendationService _recommendations;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPromotionService _promotions;
    private readonly ISetMenuService _setMenus;

    public IndexModel(ApplicationDbContext context, IRecommendationService recommendations, UserManager<ApplicationUser> userManager, IPromotionService promotions, ISetMenuService setMenus)
    {
        _context = context;
        _recommendations = recommendations;
        _userManager = userManager;
        _promotions = promotions;
        _setMenus = setMenus;
    }

    /// <summary>Which set menu is open in the large card (the others are compact cards). Defaults to the first on offer.</summary>
    [BindProperty(SupportsGet = true, Name = "set")]
    public int? SetId { get; set; }

    public List<SetMenu> Sets { get; private set; } = new();
    public SetMenu? Featured { get; private set; }

    /// <summary>Carte categories (no wine, no set-menu-only drinks) in the order the restaurant arranged them.</summary>
    public List<MenuCategory> Categories { get; private set; } = new();
    public List<MenuItem> Dishes { get; private set; } = new();
    public List<MenuItem> Picks { get; private set; } = new();
    public List<MenuItem> Tiles { get; private set; } = new();
    public List<MenuItem> Wines { get; private set; } = new();
    public Dictionary<int, decimal> Discounts { get; private set; } = new();

    public DietaryProfile? Viewer { get; private set; }
    public bool ViewerHasRestriction => Viewer?.HasAnyRestriction == true;

    /// <summary>Dishes this guest's allergies and diet rule out. Used by the "Suits my profile" chip and its count.</summary>
    public HashSet<int> NotForMe { get; private set; } = new();

    /// <summary>Visitors and customers can order; staff and administrators only look at the menu.</summary>
    public bool CanOrder => User.Identity?.IsAuthenticated != true || User.IsInRole(Roles.Customer);

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.IsInRole(Roles.Administrator)) return RedirectToPage("/Admin/MenuManagement");
        if (User.IsInRole(Roles.Staff)) return RedirectToPage("/Staff/SetMenus");
        await LoadAsync();
        return Page();
    }

    // ---------------------------------------------------------------- adding to the cart
    // The Menu page is public, but ordering is not: staff and administrators must not place orders, so anyone signed in who is
    // not a customer is refused here, whatever the buttons on the page show.

    public async Task<IActionResult> OnPostAddDishAsync(int menuItemId, string? returnTo)
    {
        if (!CanOrder) return Forbid();
        var item = await _context.MenuItems.Include(m => m.MenuCategory).FirstOrDefaultAsync(m => m.Id == menuItemId);
        if (item == null || !item.IsAvailable || item.MenuCategory is { SetMenuOnly: true })
            return Back("That dish is not available right now.", ok: false, returnTo);

        var cart = CartSession.Load(HttpContext.Session);
        CartSession.Add(cart, new CartItemVM
        {
            Kind = CartKind.Dish,
            MenuItemId = item.Id,
            Name = item.Name,
            UnitPrice = item.Price,
            Quantity = 1,
            ImageUrl = item.ImageUrl,
            CategoryName = item.MenuCategory?.Name
        });
        CartSession.Save(HttpContext.Session, cart);
        return Back($"Added {item.Name} to your order.", ok: true, returnTo);
    }

    public async Task<IActionResult> OnPostAddWineAsync(int menuItemId, string serving, string? returnTo)
    {
        if (!CanOrder) return Forbid();
        var wine = await _context.MenuItems.Include(m => m.MenuCategory).FirstOrDefaultAsync(m => m.Id == menuItemId);
        if (wine == null || !wine.IsAvailable || wine.MenuCategory?.IsWine != true)
            return Back("That wine is not available right now.", ok: false, returnTo);

        var glass = string.Equals(serving, "glass", StringComparison.OrdinalIgnoreCase);
        if (glass && !wine.GlassPrice.HasValue)
            return Back($"{wine.Name} is sold by the bottle only.", ok: false, returnTo);

        var cart = CartSession.Load(HttpContext.Session);
        CartSession.Add(cart, new CartItemVM
        {
            Kind = glass ? CartKind.WineGlass : CartKind.WineBottle,
            MenuItemId = wine.Id,
            Name = wine.Name,
            UnitPrice = glass ? wine.GlassPrice!.Value : wine.Price,
            Quantity = 1,
            ImageUrl = wine.ImageUrl,
            CategoryName = "Cellar"
        });
        CartSession.Save(HttpContext.Session, cart);
        return Back($"Added {(glass ? "a glass of" : "a bottle of")} {wine.Name} to your order.", ok: true, returnTo);
    }

    public async Task<IActionResult> OnPostAddSetAsync(int setMenuId)
    {
        if (!CanOrder) return Forbid();
        var menu = await _setMenus.GetAsync(setMenuId);
        if (menu == null || menu.Status != SetMenuStatus.Active)
            return Back("That set menu is not on offer right now.", ok: false, "sets");

        // The choices come from radio buttons and checkboxes named pick_<courseId>; anything not on the menu is rejected by the quote.
        var picks = new List<SetMenuPick>();
        foreach (var course in menu.Courses)
            foreach (var raw in Request.Form[$"pick_{course.Id}"])
                if (int.TryParse(raw, out var itemId)) picks.Add(new SetMenuPick(course.Id, itemId));

        var quote = _setMenus.Quote(menu, picks);
        if (!quote.Valid) return Back(quote.Message, ok: false, "sets", setMenuId);

        var cart = CartSession.Load(HttpContext.Session);
        CartSession.Add(cart, new CartItemVM
        {
            Kind = CartKind.SetMenu,
            SetMenuId = menu.Id,
            Name = menu.Name,
            Quantity = 1,
            UnitPrice = quote.PerGuest,
            ImageUrl = menu.ImageUrl,
            CategoryName = "Set menu",
            Picks = quote.Chosen.Select(c => new CartPickVM
            {
                CourseId = c.Course.Id,
                CourseName = c.Course.Name,
                MenuItemId = c.Dish.MenuItemId,
                Name = c.Dish.MenuItem!.Name,
                Supplement = c.Dish.Supplement
            }).ToList()
        });
        CartSession.Save(HttpContext.Session, cart);
        return Back($"Added {menu.Name} to your order.", ok: true, "sets", setMenuId);
    }

    private IActionResult Back(string message, bool ok, string? fragment, int? set = null)
    {
        // The page's script adds to the cart without reloading (so the guest stays where they were and sees it go in);
        // it asks for JSON. Without scripts the form posts as usual and comes back to the same section.
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var count = CartSession.Load(HttpContext.Session).Sum(c => c.Quantity);
            return new JsonResult(new { ok, message, cartCount = count, cartUrl = Url.Page("/Orders/Cart") }) { StatusCode = ok ? 200 : 400 };
        }
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage(null, null, new { set = set ?? SetId }, string.IsNullOrWhiteSpace(fragment) ? "carte" : fragment);
    }

    private async Task LoadAsync()
    {
        Sets = await _setMenus.ListForGuestsAsync();
        Featured = Sets.FirstOrDefault(s => s.Id == SetId) ?? Sets.FirstOrDefault();

        Categories = await _context.MenuCategories.AsNoTracking()
            .Where(c => c.IsActive && !c.IsWine && !c.SetMenuOnly).OrderBy(c => c.DisplayOrder).ToListAsync();

        var all = await _context.MenuItems.AsNoTracking().Include(m => m.MenuCategory)
            .Where(m => m.IsAvailable && m.MenuCategory!.IsActive).ToListAsync();
        Dishes = all.Where(m => m.MenuCategory is { IsWine: false, SetMenuOnly: false })
            .OrderBy(m => m.MenuCategory!.DisplayOrder).ThenBy(m => m.Name).ToList();
        Wines = all.Where(m => m.MenuCategory?.IsWine == true)
            .OrderBy(m => m.MenuCategory!.DisplayOrder).ThenByDescending(m => m.Price).ToList();

        // Dietary profile: customers only. Staff keep a dietary record for staff-meal planning; it never shapes this page.
        if (User.IsInRole(Roles.Customer))
        {
            var me = await _userManager.GetUserAsync(User);
            if (me != null) Viewer = DietaryRules.ProfileFor(me);
        }
        if (ViewerHasRestriction)
            NotForMe = Dishes.Where(d => !DietaryRules.IsSuitable(d, Viewer!)).Select(d => d.Id).ToHashSet();

        Picks = Dishes.Where(d => d.IsChefsPick).Take(2).ToList();
        Tiles = Dishes.Where(d => !Picks.Contains(d)).ToList();

        var viewerId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;
        var viewerEmail = viewerId != null ? await _context.Users.Where(u => u.Id == viewerId).Select(u => u.Email).FirstOrDefaultAsync() : null;
        Discounts = await _promotions.GetDisplayDiscountsAsync(Dishes, viewerId, viewerEmail);
    }
}
