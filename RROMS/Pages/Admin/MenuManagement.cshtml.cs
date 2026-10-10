using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// 
/// The dishes, the wine list and the categories they sit in. A dish with a photo shows it to guests; without one guests see the
/// house plate. A dish that is part of a set menu, or has been ordered before, is never deleted from under them: it is refused or
/// switched off instead.
/// 
[RequestSizeLimit(6 * 1024 * 1024)] // uploads are capped at 4 MB by IAdminImageService; this just stops oversized bodies early
public class MenuManagementModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IAdminImageService _images;

    public MenuManagementModel(ApplicationDbContext context, IAdminImageService images)
    {
        _context = context;
        _images = images;
    }

    /// "items", "wine" or "categories".
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "items";
    [BindProperty(SupportsGet = true)] public int? Cat { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    [BindProperty] public ItemInput Item { get; set; } = new();
    [BindProperty] public CategoryInput Category { get; set; } = new();

    public class ItemInput
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Price { get; set; }
        public decimal? GlassPrice { get; set; }
        public int MenuCategoryId { get; set; }
        public int? Calories { get; set; }
        public string? Description { get; set; }
        public string? WineNote { get; set; }
        public bool IsAvailable { get; set; } = true;
        public bool IsVegetarian { get; set; }
        public bool IsSpicy { get; set; }
        public bool IsChefsPick { get; set; }
        public string[]? Allergens { get; set; }
        public bool AllergensReviewed { get; set; }
        public string[]? DietTags { get; set; }
        public IFormFile? Photo { get; set; }
        public bool RemovePhoto { get; set; }
    }

    public class CategoryInput
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsWine { get; set; }
        public bool SetMenuOnly { get; set; }
    }

    public List<MenuCategory> Categories { get; private set; } = new();
    public List<MenuItem> Rows { get; private set; } = new();
    public Dictionary<int, int> ItemCounts { get; private set; } = new();
    public int DishCount { get; private set; }
    public int WineCount { get; private set; }
    public int ShownOf { get; private set; }
    public bool OpenItem { get; private set; }
    public bool OpenCategory { get; private set; }
    public string? ItemError { get; private set; }
    public string? CategoryError { get; private set; }

    /// Categories the dish drawer can choose from, newest order first within the list.
    public List<MenuCategory> ItemCategories => Categories.Where(c => c.IsActive).ToList();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (Tab != "wine" && Tab != "categories") Tab = "items";
        Categories = await _context.MenuCategories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync();
        ItemCounts = await _context.MenuItems.AsNoTracking().GroupBy(m => m.MenuCategoryId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

        var wineIds = Categories.Where(c => c.IsWine).Select(c => c.Id).ToHashSet();
        WineCount = ItemCounts.Where(kv => wineIds.Contains(kv.Key)).Sum(kv => kv.Value);
        DishCount = ItemCounts.Where(kv => !wineIds.Contains(kv.Key)).Sum(kv => kv.Value);

        if (Tab == "categories") return;
        var q = _context.MenuItems.AsNoTracking().Include(m => m.MenuCategory)
            .Where(m => Tab == "wine" ? m.MenuCategory!.IsWine : !m.MenuCategory!.IsWine);
        var inTab = await q.CountAsync();
        if (Cat.HasValue) q = q.Where(m => m.MenuCategoryId == Cat.Value);
        if (!string.IsNullOrWhiteSpace(Q)) { var term = Q.Trim(); q = q.Where(m => m.Name.Contains(term)); }
        Rows = await q.OrderBy(m => m.MenuCategory!.DisplayOrder).ThenBy(m => m.Name).ToListAsync();
        ShownOf = inTab;
    }

    // ------------------------------------------------------------------------------------------ dishes and wines

    public async Task<IActionResult> OnPostSaveItemAsync()
    {
        var f = Item;
        var name = (f.Name ?? "").Trim();
        var category = await _context.MenuCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == f.MenuCategoryId);
        string? problem = null;
        if (name.Length == 0 || name.Length > 100) problem = "Please enter a name (up to 100 characters).";
        else if (category == null) problem = "Please choose a category.";
        else if (f.Description != null && f.Description.Length > 500) problem = "Keep the description under 500 characters.";
        else if (f.Price < 0 || f.Price > 10000) problem = category.IsWine ? "Enter a bottle price between 0 and 10,000." : "Enter a price between 0 and 10,000.";
        else if (category.IsWine && f.GlassPrice.HasValue && (f.GlassPrice < 0 || f.GlassPrice > 10000)) problem = "Enter a glass price between 0 and 10,000, or leave it empty for bottle only.";
        else if (category.IsWine && f.GlassPrice.HasValue && f.GlassPrice >= f.Price && f.Price > 0) problem = "A glass should cost less than the bottle.";
        else if (f.Calories.HasValue && (f.Calories < 0 || f.Calories > 10000)) problem = "Calories must be between 0 and 10,000.";
        else if (category.IsWine && f.WineNote != null && f.WineNote.Length > 80) problem = "The line under the wine's name can be 80 characters at most.";

        MenuItem? item = null;
        if (problem == null && f.Id != 0)
        {
            item = await _context.MenuItems.FirstOrDefaultAsync(m => m.Id == f.Id);
            if (item == null) problem = "That item no longer exists.";
        }

        string? photoUrl = null;
        if (problem == null)
        {
            var existing = f.RemovePhoto ? null : item?.ImageUrl;
            var (ok, message, url) = await _images.ResolveAsync(f.Photo, existing, "menu");
            if (!ok) problem = message; else photoUrl = url;
        }

        if (problem != null)
        {
            ItemError = problem;
            OpenItem = true;
            await LoadAsync();
            return Page();
        }

        var isNew = item == null;
        item ??= new MenuItem { CreatedAt = DateTime.UtcNow };
        var previousImage = item.ImageUrl;
        item.Name = name;
        item.Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim();
        item.Price = f.Price;
        item.MenuCategoryId = f.MenuCategoryId;
        item.ImageUrl = photoUrl;
        item.IsAvailable = f.IsAvailable;
        item.IsVegetarian = f.IsVegetarian;
        item.IsSpicy = f.IsSpicy;
        item.IsChefsPick = f.IsChefsPick;
        item.Calories = f.Calories;
        item.GlassPrice = category!.IsWine ? f.GlassPrice : null;
        item.WineNote = category.IsWine && !string.IsNullOrWhiteSpace(f.WineNote) ? f.WineNote.Trim() : null;
        item.Allergens = BuildAllergens(f.Allergens, f.AllergensReviewed);
        item.DietaryTags = BuildDietTags(f.DietTags);
        if (isNew) _context.MenuItems.Add(item);
        await _context.SaveChangesAsync();

        if (!isNew && previousImage != item.ImageUrl) _images.DeleteIfOurs(previousImage);

        TempData["StatusMessage"] = isNew ? $"{name} was added to the {(category.IsWine ? "wine list" : "menu")}." : $"{name} was saved.";
        return RedirectToPage(new { tab = category.IsWine ? "wine" : "items" });
    }

    /// 
    /// Only values from the fixed allergen list are stored (anything else a tampered form sends is dropped). "Not recorded" (null)
    /// and "recorded as none" ("") are different on purpose: the guest filters never treat an unrecorded dish as safe, so the
    /// kitchen has to tick "checked" before a dish can count as allergen-free.
    /// 
    private static string? BuildAllergens(string[]? allergens, bool reviewed)
    {
        var joined = DietaryRules.JoinList(allergens, DietaryRules.AllergenNames);
        return reviewed || joined.Length > 0 ? joined : null;
    }

    private static string? BuildDietTags(string[]? dietTags)
    {
        var joined = DietaryRules.JoinList(dietTags, DietaryRules.DietTags);
        return joined.Length > 0 ? joined : null;
    }

    /// The "On the menu" switch in the list: sold out for tonight, or back again. Sends the wanted state, so repeating it is harmless.
    public async Task<IActionResult> OnPostAvailabilityAsync(int id, bool available)
    {
        var item = await _context.MenuItems.FindAsync(id);
        if (item == null) { TempData["ErrorMessage"] = "That item no longer exists."; return RedirectToPage(); }
        item.IsAvailable = available;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = available ? $"{item.Name} is on the menu again." : $"{item.Name} is off the menu until you switch it back on.";
        return RedirectToPage(new { tab = Tab, cat = Cat, q = Q });
    }

    public async Task<IActionResult> OnPostDeleteItemAsync(int id)
    {
        var item = await _context.MenuItems.Include(m => m.MenuCategory).FirstOrDefaultAsync(m => m.Id == id);
        if (item == null) return RedirectToPage();
        var tab = item.MenuCategory?.IsWine == true ? "wine" : "items";

        var setNames = await _context.SetMenuDishes.Where(d => d.MenuItemId == id).Select(d => d.Course!.SetMenu!.Name).Distinct().ToListAsync();
        if (setNames.Count > 0)
        {
            TempData["ErrorMessage"] = $"{item.Name} is part of {string.Join(" and ", setNames.Select(n => "“" + n + "”"))}. Take it out of the set menu first, or switch it off here.";
            return RedirectToPage(new { tab });
        }

        if (await _context.OrderItems.AnyAsync(oi => oi.MenuItemId == id))
        {
            // Keep order history intact: switch it off instead of deleting.
            item.IsAvailable = false;
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = $"{item.Name} has been ordered before, so it was switched off instead of removed. Past orders keep their record.";
            return RedirectToPage(new { tab });
        }

        _context.MenuItems.Remove(item);
        await _context.SaveChangesAsync();
        _images.DeleteIfOurs(item.ImageUrl);
        TempData["StatusMessage"] = $"{item.Name} was removed.";
        return RedirectToPage(new { tab });
    }

    // ------------------------------------------------------------------------------------------ categories

    public async Task<IActionResult> OnPostSaveCategoryAsync()
    {
        var c = Category;
        var name = (c.Name ?? "").Trim();
        string? problem = null;
        if (name.Length == 0 || name.Length > 60) problem = "Please enter a name (up to 60 characters).";
        else if (c.Description != null && c.Description.Length > 200) problem = "Keep the description under 200 characters.";
        else if (c.IsWine && c.SetMenuOnly) problem = "A category is either the wine list or served only in set menus, not both.";
        else if (c.DisplayOrder < 0 || c.DisplayOrder > 1000) problem = "The order should be between 0 and 1,000.";
        else if (await _context.MenuCategories.AnyAsync(x => x.Name == name && x.Id != c.Id)) problem = "There is already a category with that name.";

        MenuCategory? cat = null;
        if (problem == null && c.Id != 0)
        {
            cat = await _context.MenuCategories.FirstOrDefaultAsync(x => x.Id == c.Id);
            if (cat == null) problem = "That category no longer exists.";
            // Changing whether a category is the wine list changes how its prices read, so it is only allowed while it is empty.
            else if (cat.IsWine != c.IsWine && await _context.MenuItems.AnyAsync(m => m.MenuCategoryId == cat.Id))
                problem = "A category that already has items cannot be switched to or from the wine list. Move the items first.";
        }
        if (problem != null)
        {
            CategoryError = problem;
            OpenCategory = true;
            Tab = "categories";
            await LoadAsync();
            return Page();
        }

        var isNew = cat == null;
        cat ??= new MenuCategory();
        cat.Name = name;
        cat.Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim();
        cat.DisplayOrder = c.DisplayOrder;
        cat.IsActive = c.IsActive;
        cat.IsWine = c.IsWine;
        cat.SetMenuOnly = c.SetMenuOnly;
        if (isNew) _context.MenuCategories.Add(cat);
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = isNew ? $"The category {name} was added." : $"The category {name} was saved.";
        return RedirectToPage(new { tab = "categories" });
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        var cat = await _context.MenuCategories.FindAsync(id);
        if (cat == null) return RedirectToPage(new { tab = "categories" });
        if (await _context.MenuItems.AnyAsync(m => m.MenuCategoryId == id))
            TempData["ErrorMessage"] = $"{cat.Name} still has items. Move or remove them first.";
        else if (await _context.Promotions.AnyAsync(p => p.MenuCategoryId == id))
            TempData["ErrorMessage"] = $"{cat.Name} is used by a promotion. Change the promotion first.";
        else
        {
            _context.MenuCategories.Remove(cat);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = $"The category {cat.Name} was removed.";
        }
        return RedirectToPage(new { tab = "categories" });
    }
}
