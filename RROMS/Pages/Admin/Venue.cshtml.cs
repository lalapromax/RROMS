using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Pages.Admin;

public class VenueModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public VenueModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<DiningArea> DiningAreas { get; set; } = new();
    public List<RestaurantTable> Tables { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        DiningAreas = await _context.DiningAreas.OrderBy(d => d.Name).ToListAsync();
        Tables = await _context.RestaurantTables.Include(t => t.DiningArea).OrderBy(t => t.TableNumber).ToListAsync();
    }

    public async Task<IActionResult> OnPostAddAreaAsync(string name, string? description)
    {
        _context.DiningAreas.Add(new DiningArea { Name = name, Description = description, IsActive = true });
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Dining area added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAreaAsync(int id, string name, string? description, bool isActive)
    {
        var area = await _context.DiningAreas.FindAsync(id);
        if (area == null) { TempData["ErrorMessage"] = "Dining area not found."; return RedirectToPage(); }

        area.Name = name;
        area.Description = description;
        area.IsActive = isActive;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Dining area updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAreaAsync(int id)
    {
        var hasTables = await _context.RestaurantTables.AnyAsync(t => t.DiningAreaId == id);
        if (hasTables)
        {
            TempData["ErrorMessage"] = "This dining area still has tables assigned - remove or reassign them first.";
            return RedirectToPage();
        }

        var area = await _context.DiningAreas.FindAsync(id);
        if (area != null)
        {
            _context.DiningAreas.Remove(area);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Dining area removed.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddTableAsync(string tableNumber, int diningAreaId, int capacity)
    {
        _context.RestaurantTables.Add(new RestaurantTable
        {
            TableNumber = tableNumber,
            DiningAreaId = diningAreaId,
            Capacity = capacity,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Table added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditTableAsync(int id, string tableNumber, int diningAreaId, int capacity, bool isActive)
    {
        var table = await _context.RestaurantTables.FindAsync(id);
        if (table == null) { TempData["ErrorMessage"] = "Table not found."; return RedirectToPage(); }

        table.TableNumber = tableNumber;
        table.DiningAreaId = diningAreaId;
        table.Capacity = capacity;
        table.IsActive = isActive;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = "Table updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTableAsync(int id)
    {
        var table = await _context.RestaurantTables.FindAsync(id);
        if (table != null)
        {
            _context.RestaurantTables.Remove(table);
            await _context.SaveChangesAsync();
            TempData["StatusMessage"] = "Table removed.";
        }
        return RedirectToPage();
    }
}
