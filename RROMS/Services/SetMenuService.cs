using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services;

/// One choice a guest makes on a set menu: this dish, in this course.
public record SetMenuPick(int CourseId, int MenuItemId);

/// What a set menu costs per guest for a given set of choices.
public record SetMenuQuote(bool Valid, string Message, decimal SetPrice, decimal Supplements, List<(SetMenuCourse Course, SetMenuDish Dish)> Chosen)
{
    public decimal PerGuest => SetPrice + Supplements;
}

public interface ISetMenuService
{
    /// Active set menus offered on the given day (all active ones when day is null), in the order staff arranged them.
    Task<List<SetMenu>> ListForGuestsAsync(DayOfWeek? day = null);

    /// Every set menu, including drafts and hidden ones, for the staff and administrator screens.
    Task<List<SetMenu>> ListAllAsync();

    Task<SetMenu?> GetAsync(int id);

    /// Checks the picks against the menu's courses and prices them. Used by the cart, by checkout and by the booking page.
    Task<SetMenuQuote> QuoteAsync(int setMenuId, IEnumerable<SetMenuPick> picks, bool guestFacing = true);

    /// The same check against a menu that is already loaded.
    SetMenuQuote Quote(SetMenu menu, IEnumerable<SetMenuPick> picks);

    Task<(bool Success, string Message, SetMenu? Menu)> SaveAsync(SetMenuEdit edit, string? userId);
    Task<(bool Success, string Message)> SetStatusAsync(int id, SetMenuStatus status);
    Task<(bool Success, string Message)> DeleteAsync(int id);
}

/// What the set menu builder posts. Courses carry dish ids and their extra charges.
public class SetMenuEdit
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    public string? Description { get; set; }
    public decimal PricePerGuest { get; set; }
    public string? ImageUrl { get; set; }
    public SetMenuStatus Status { get; set; } = SetMenuStatus.Draft;
    public List<DayOfWeek> Days { get; set; } = new();
    public bool ServedAtLunch { get; set; }
    public bool ServedAtDinner { get; set; } = true;
    public List<SetMenuCourseEdit> Courses { get; set; } = new();
}

public class SetMenuCourseEdit
{
    public string Name { get; set; } = string.Empty;
    public int Picks { get; set; } = 1;
    public List<SetMenuDishEdit> Dishes { get; set; } = new();
}

public class SetMenuDishEdit
{
    public int MenuItemId { get; set; }
    public decimal Supplement { get; set; }
}

public class SetMenuService : ISetMenuService
{
    private readonly ApplicationDbContext _db;
    public SetMenuService(ApplicationDbContext db) => _db = db;

    private IQueryable<SetMenu> Full() => _db.SetMenus
        .Include(s => s.Courses.OrderBy(c => c.SortOrder)).ThenInclude(c => c.Dishes.OrderBy(d => d.SortOrder)).ThenInclude(d => d.MenuItem).ThenInclude(m => m!.MenuCategory);

    public async Task<List<SetMenu>> ListForGuestsAsync(DayOfWeek? day = null)
    {
        var list = await Full().AsNoTracking().Where(s => s.Status == SetMenuStatus.Active).OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync();
        // A dish that is sold out is removed from the choices; a course left with no dishes hides the whole menu from guests
        // (they cannot complete it), which is why it is dropped here rather than shown half empty.
        foreach (var s in list)
            foreach (var c in s.Courses)
                c.Dishes = c.Dishes.Where(d => d.MenuItem != null && d.MenuItem.IsAvailable).ToList();
        list = list.Where(s => s.Courses.Count > 0 && s.Courses.All(c => c.Dishes.Count > 0)).ToList();
        if (day.HasValue) list = list.Where(s => s.OffersOn(day.Value)).ToList();
        return list;
    }

    public Task<List<SetMenu>> ListAllAsync() =>
        Full().AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync();

    public Task<SetMenu?> GetAsync(int id) => Full().FirstOrDefaultAsync(s => s.Id == id);

    public async Task<SetMenuQuote> QuoteAsync(int setMenuId, IEnumerable<SetMenuPick> picks, bool guestFacing = true)
    {
        var menu = await Full().AsNoTracking().FirstOrDefaultAsync(s => s.Id == setMenuId);
        if (menu == null) return new SetMenuQuote(false, "That set menu is no longer on offer.", 0, 0, new());
        if (guestFacing && menu.Status != SetMenuStatus.Active) return new SetMenuQuote(false, "That set menu is not on offer right now.", 0, 0, new());
        return Quote(menu, picks);
    }

    public SetMenuQuote Quote(SetMenu menu, IEnumerable<SetMenuPick> picks)
    {
        var chosen = new List<(SetMenuCourse, SetMenuDish)>();
        var pickList = picks.ToList();
        decimal supplements = 0;
        foreach (var course in menu.Courses.OrderBy(c => c.SortOrder))
        {
            var forCourse = pickList.Where(p => p.CourseId == course.Id).Select(p => p.MenuItemId).Distinct().ToList();
            var need = course.Picks == 0 ? course.Dishes.Count : course.Picks;
            if (forCourse.Count != need)
                return new SetMenuQuote(false, need == 1 ? $"Choose one for {course.Name.ToLowerInvariant()}." : $"Choose {need} for {course.Name.ToLowerInvariant()}.", 0, 0, new());
            foreach (var itemId in forCourse)
            {
                var dish = course.Dishes.FirstOrDefault(d => d.MenuItemId == itemId);
                if (dish == null || dish.MenuItem == null || !dish.MenuItem.IsAvailable)
                    return new SetMenuQuote(false, $"One of your {course.Name.ToLowerInvariant()} choices is no longer available.", 0, 0, new());
                chosen.Add((course, dish));
                supplements += dish.Supplement;
            }
        }
        return new SetMenuQuote(true, string.Empty, menu.PricePerGuest, supplements, chosen);
    }

    public async Task<(bool Success, string Message, SetMenu? Menu)> SaveAsync(SetMenuEdit edit, string? userId)
    {
        var name = (edit.Name ?? string.Empty).Trim();
        if (name.Length == 0) return (false, "Give the set menu a name.", null);
        if (edit.PricePerGuest < 0) return (false, "The price cannot be negative.", null);
        if (edit.Courses.Count == 0) return (false, "Add at least one course.", null);
        foreach (var c in edit.Courses)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) return (false, "Every course needs a name.", null);
            if (c.Dishes.Count == 0) return (false, $"Add at least one dish to {c.Name}.", null);
            if (c.Picks < 0 || c.Picks > Math.Max(1, c.Dishes.Count)) return (false, $"{c.Name}: the number of picks cannot be more than the number of dishes.", null);
        }
        if (!edit.ServedAtLunch && !edit.ServedAtDinner) return (false, "Choose lunch, dinner or both.", null);
        if (edit.Days.Count == 0) return (false, "Choose at least one day.", null);

        var allIds = edit.Courses.SelectMany(c => c.Dishes.Select(d => d.MenuItemId)).Distinct().ToList();
        var found = await _db.MenuItems.Where(m => allIds.Contains(m.Id)).Select(m => m.Id).ToListAsync();
        if (found.Count != allIds.Count) return (false, "One of the dishes no longer exists.", null);

        SetMenu menu;
        if (edit.Id.HasValue)
        {
            menu = await _db.SetMenus.Include(s => s.Courses).ThenInclude(c => c.Dishes).FirstOrDefaultAsync(s => s.Id == edit.Id.Value)
                   ?? throw new InvalidOperationException("Set menu not found.");
            _db.SetMenuDishes.RemoveRange(menu.Courses.SelectMany(c => c.Dishes));
            _db.SetMenuCourses.RemoveRange(menu.Courses);
            menu.Courses = new List<SetMenuCourse>();
        }
        else
        {
            menu = new SetMenu { CreatedById = userId, SortOrder = (await _db.SetMenus.MaxAsync(s => (int?)s.SortOrder) ?? 0) + 1 };
            _db.SetMenus.Add(menu);
        }

        menu.Name = name;
        menu.Tagline = string.IsNullOrWhiteSpace(edit.Tagline) ? null : edit.Tagline.Trim();
        menu.Description = string.IsNullOrWhiteSpace(edit.Description) ? null : edit.Description.Trim();
        menu.PricePerGuest = Math.Round(edit.PricePerGuest, 2);
        menu.ImageUrl = string.IsNullOrWhiteSpace(edit.ImageUrl) ? null : edit.ImageUrl;
        menu.Status = edit.Status;
        menu.DaysMask = SetMenu.MaskOf(edit.Days);
        menu.ServedAtLunch = edit.ServedAtLunch;
        menu.ServedAtDinner = edit.ServedAtDinner;
        menu.UpdatedAt = DateTime.UtcNow;

        var order = 0;
        foreach (var c in edit.Courses)
        {
            var course = new SetMenuCourse { Name = c.Name.Trim(), Picks = c.Picks, SortOrder = ++order };
            var d = 0;
            foreach (var dish in c.Dishes)
                course.Dishes.Add(new SetMenuDish { MenuItemId = dish.MenuItemId, Supplement = Math.Max(0, dish.Supplement), SortOrder = ++d });
            menu.Courses.Add(course);
        }
        await _db.SaveChangesAsync();
        return (true, edit.Id.HasValue ? "Set menu saved." : "Set menu created.", menu);
    }

    public async Task<(bool Success, string Message)> SetStatusAsync(int id, SetMenuStatus status)
    {
        var menu = await _db.SetMenus.FindAsync(id);
        if (menu == null) return (false, "That set menu no longer exists.");
        menu.Status = status;
        menu.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, status switch { SetMenuStatus.Active => "Set menu is now on offer.", SetMenuStatus.Hidden => "Set menu hidden from guests.", _ => "Set menu moved to drafts." });
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int id)
    {
        var menu = await _db.SetMenus.Include(s => s.Courses).ThenInclude(c => c.Dishes).FirstOrDefaultAsync(s => s.Id == id);
        if (menu == null) return (false, "That set menu no longer exists.");
        // Past orders and reservations keep their own copy of the name, so deleting never rewrites history.
        var inUse = await _db.Reservations.Where(r => r.SetMenuId == id).ToListAsync();
        foreach (var r in inUse) r.SetMenuId = null;
        _db.SetMenuDishes.RemoveRange(menu.Courses.SelectMany(c => c.Dishes));
        _db.SetMenuCourses.RemoveRange(menu.Courses);
        _db.SetMenus.Remove(menu);
        await _db.SaveChangesAsync();
        return (true, "Set menu deleted.");
    }
}
