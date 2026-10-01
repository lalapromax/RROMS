using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Pages.Admin;

public class MenuManagementModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public MenuManagementModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<MenuCategory> Categories { get; set; } = new();
    public List<MenuItem> Items { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Categories = await _context.MenuCategories.OrderBy(c => c.DisplayOrder).ToListAsync();
        Items = await _context.MenuItems.Include(m => m.MenuCategory).OrderBy(m => m.MenuCategory!.DisplayOrder).ThenBy(m => m.Name).ToListAsync();
    }

    public async Task<IActionResult> OnPostAddCategoryAsync(string name, string? description, int displayOrder)
    {
        _context.MenuCategories.Add(new MenuCategory { Name = name, Description = description, DisplayOrder = displayOrder, IsActive = true });
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Category added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditCategoryAsync(int id, string name, string? description, int displayOrder, bool isActive)
    {
        var category = await _context.MenuCategories.FindAsync(id);
        if (category == null) { TempData["ErrorMessage"] = "Category not found."; return RedirectToPage(); }

        category.Name = name;
        category.Description = description;
        category.DisplayOrder = displayOrder;
        category.IsActive = isActive;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Category updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        var hasItems = await _context.MenuItems.AnyAsync(m => m.MenuCategoryId == id);
        if (hasItems)
        {
            TempData["ErrorMessage"] = "This category still has menu items - move or delete them first.";
            return RedirectToPage();
        }
        var category = await _context.MenuCategories.FindAsync(id);
        if (category != null)
        {
            _context.MenuCategories.Remove(category);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Category removed.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddItemAsync(string name, string? description, decimal price, int menuCategoryId,
        string? imageUrl, bool isAvailable, bool isVegetarian, bool isSpicy, int? calories)
    {
        _context.MenuItems.Add(new MenuItem
        {
            Name = name,
            Description = description,
            Price = price,
            MenuCategoryId = menuCategoryId,
            ImageUrl = imageUrl,
            IsAvailable = isAvailable,
            IsVegetarian = isVegetarian,
            IsSpicy = isSpicy,
            Calories = calories,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Menu item added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditItemAsync(int id, string name, string? description, decimal price, int menuCategoryId,
        string? imageUrl, bool isAvailable, bool isVegetarian, bool isSpicy, int? calories)
    {
        var item = await _context.MenuItems.FindAsync(id);
        if (item == null) { TempData["ErrorMessage"] = "Menu item not found."; return RedirectToPage(); }

        item.Name = name;
        item.Description = description;
        item.Price = price;
        item.MenuCategoryId = menuCategoryId;
        item.ImageUrl = imageUrl;
        item.IsAvailable = isAvailable;
        item.IsVegetarian = isVegetarian;
        item.IsSpicy = isSpicy;
        item.Calories = calories;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Menu item updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteItemAsync(int id)
    {
        var isUsedInOrders = await _context.OrderItems.AnyAsync(oi => oi.MenuItemId == id);
        if (isUsedInOrders)
        {
            // Keep order history intact - mark unavailable instead of deleting.
            var item = await _context.MenuItems.FindAsync(id);
            if (item != null)
            {
                item.IsAvailable = false;
                await _context.SaveChangesAsync();
                TempData["StatusMessage"] = "This item has past orders, so it was marked unavailable instead of deleted.";
            }
            return RedirectToPage();
        }

        var toDelete = await _context.MenuItems.FindAsync(id);
        if (toDelete != null)
        {
            _context.MenuItems.Remove(toDelete);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Menu item removed.";
        }
        return RedirectToPage();
    }
}
