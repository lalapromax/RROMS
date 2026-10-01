using System.Text.Json;
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
    private const string CartSessionKey = "Cart";

    private readonly ApplicationDbContext _context;
    private readonly IRecommendationService _recommendationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext context, IRecommendationService recommendationService, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _recommendationService = recommendationService;
        _userManager = userManager;
    }

    public List<MenuCategory> Categories { get; set; } = new();
    public List<MenuItem> Items { get; set; } = new();
    public List<MenuItem> Recommended { get; set; } = new();
    public Dictionary<int, decimal> ActivePromotionDiscounts { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? CategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool VegetarianOnly { get; set; }

    public int CartItemCount { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAddToCartAsync(int menuItemId, int quantity, string? specialInstructions)
    {
        if (quantity < 1) quantity = 1;

        var menuItem = await _context.MenuItems.FindAsync(menuItemId);
        if (menuItem == null || !menuItem.IsAvailable)
        {
            TempData["ErrorMessage"] = "That item is no longer available.";
            return RedirectToPage(new { CategoryId, Search, VegetarianOnly });
        }

        var cart = GetCart();
        var existing = cart.FirstOrDefault(c => c.MenuItemId == menuItemId && c.SpecialInstructions == specialInstructions);
        if (existing != null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            cart.Add(new CartItemVM
            {
                MenuItemId = menuItem.Id,
                Name = menuItem.Name,
                UnitPrice = menuItem.Price,
                Quantity = quantity,
                SpecialInstructions = specialInstructions,
                ImageUrl = menuItem.ImageUrl
            });
        }
        SaveCart(cart);

        TempData["StatusMessage"] = $"Added {menuItem.Name} to your cart.";
        return RedirectToPage(new { CategoryId, Search, VegetarianOnly });
    }

    private async Task LoadAsync()
    {
        Categories = await _context.MenuCategories.Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).ToListAsync();

        var query = _context.MenuItems.Include(m => m.MenuCategory).Where(m => m.IsAvailable).AsQueryable();
        if (CategoryId.HasValue)
            query = query.Where(m => m.MenuCategoryId == CategoryId.Value);
        if (!string.IsNullOrWhiteSpace(Search))
            query = query.Where(m => m.Name.Contains(Search) || (m.Description != null && m.Description.Contains(Search)));
        if (VegetarianOnly)
            query = query.Where(m => m.IsVegetarian);

        Items = await query.OrderBy(m => m.MenuCategory!.DisplayOrder).ThenBy(m => m.Name).ToListAsync();

        var activePromotions = await _context.Promotions
            .Where(p => p.IsActive && p.MenuItemId != null && p.StartDate <= DateTime.UtcNow.Date && p.EndDate >= DateTime.UtcNow.Date)
            .ToListAsync();
        ActivePromotionDiscounts = activePromotions
            .Where(p => p.MenuItemId.HasValue)
            .GroupBy(p => p.MenuItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(p => p.DiscountPercent));

        string? userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;
        Recommended = await _recommendationService.GetRecommendationsAsync(userId, 4);

        CartItemCount = GetCart().Sum(c => c.Quantity);
    }

    private List<CartItemVM> GetCart()
    {
        var json = HttpContext.Session.GetString(CartSessionKey);
        if (string.IsNullOrEmpty(json)) return new List<CartItemVM>();
        return JsonSerializer.Deserialize<List<CartItemVM>>(json) ?? new List<CartItemVM>();
    }

    private void SaveCart(List<CartItemVM> cart)
    {
        HttpContext.Session.SetString(CartSessionKey, JsonSerializer.Serialize(cart));
    }
}
