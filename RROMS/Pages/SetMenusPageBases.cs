using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;

namespace RROMS.Pages;

/// 
/// Staff and administrators manage set menus with the same screens, so the behaviour lives here once and the two folders
/// (Pages/Staff and Pages/Admin) hold thin pages that choose their own layout and URL. A set menu is a draft until published;
/// hiding it takes it off the guest pages without losing it.
/// 
public abstract class SetMenusListPageBase : PageModel
{
    private readonly ISetMenuService _sets;
    protected SetMenusListPageBase(ISetMenuService sets) => _sets = sets;

    /// "Staff" or "Admin": which folder this page lives in, used to build links to its sibling pages.
    public abstract string Area { get; }

    public List<SetMenu> Menus { get; private set; } = new();

    public async Task OnGetAsync() => Menus = await _sets.ListAllAsync();

    /// to = active | hidden | draft.
    public async Task<IActionResult> OnPostStatusAsync(int id, string to)
    {
        var status = to switch
        {
            "active" => SetMenuStatus.Active,
            "hidden" => SetMenuStatus.Hidden,
            "draft" => SetMenuStatus.Draft,
            _ => (SetMenuStatus?)null
        };
        if (status == null) return RedirectToPage();
        var (ok, message) = await _sets.SetStatusAsync(id, status.Value);
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage();
    }
}

/// One dish the builder can put in a course: what the picker lists and what the preview and price check need.
public record SetMenuItemOption(int Id, string Name, string Category, decimal Price, bool HasPhoto, bool Available, string Pic);

public abstract class SetMenuEditPageBase : PageModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ISetMenuService _sets;
    private readonly ApplicationDbContext _context;
    private readonly IAdminImageService _images;

    protected SetMenuEditPageBase(ISetMenuService sets, ApplicationDbContext context, IAdminImageService images)
    {
        _sets = sets;
        _context = context;
        _images = images;
    }

    public abstract string Area { get; }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }

    /// The whole menu as the builder holds it: name, price, days, courses and dishes. One field, checked on the server.
    [BindProperty] public string? Payload { get; set; }
    /// draft | publish | save | hide
    [BindProperty] public string Intent { get; set; } = "draft";
    [BindProperty] public IFormFile? Photo { get; set; }
    [BindProperty] public bool RemovePhoto { get; set; }

    public string StateJson { get; private set; } = "{}";
    public string ItemsJson { get; private set; } = "[]";
    public SetMenuStatus Status { get; private set; } = SetMenuStatus.Draft;
    public string? PhotoUrl { get; private set; }
    public string? Problem { get; private set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        SetMenu? menu = null;
        if (Id.HasValue)
        {
            menu = await _sets.GetAsync(Id.Value);
            if (menu == null) return NotFound();
        }
        Status = menu?.Status ?? SetMenuStatus.Draft;
        PhotoUrl = menu?.ImageUrl;
        StateJson = JsonSerializer.Serialize(menu == null ? EmptyState() : StateOf(menu), Json);
        await LoadItemsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SetMenu? existing = null;
        if (Id.HasValue)
        {
            existing = await _sets.GetAsync(Id.Value);
            if (existing == null) return NotFound();
        }
        Status = existing?.Status ?? SetMenuStatus.Draft;
        PhotoUrl = existing?.ImageUrl;

        var problem = Read(out var state);
        if (problem == null)
        {
            // draft / publish / save (keep as it is) / hide
            var status = Intent switch
            {
                "publish" => SetMenuStatus.Active,
                "hide" => SetMenuStatus.Hidden,
                "save" => existing?.Status ?? SetMenuStatus.Draft,
                _ => SetMenuStatus.Draft
            };

            var (photoOk, photoMessage, photoUrl) = await _images.ResolveAsync(Photo, RemovePhoto ? null : existing?.ImageUrl, "menu");
            if (!photoOk) problem = photoMessage;
            else
            {
                var edit = new SetMenuEdit
                {
                    Id = existing?.Id,
                    Name = state!.Name ?? "",
                    Tagline = state.Tagline,
                    Description = state.Description,
                    PricePerGuest = state.Price,
                    ImageUrl = photoUrl,
                    Status = status,
                    Days = state.Days.Where(d => d is >= 0 and <= 6).Distinct().Select(d => (DayOfWeek)d).ToList(),
                    ServedAtLunch = state.Lunch,
                    ServedAtDinner = state.Dinner,
                    Courses = state.Courses.Select(c => new SetMenuCourseEdit
                    {
                        Name = c.Name ?? "",
                        Picks = c.Picks,
                        Dishes = c.Dishes.GroupBy(d => d.Id).Select(g => g.First())
                            .Select(d => new SetMenuDishEdit { MenuItemId = d.Id, Supplement = d.Supplement }).ToList()
                    }).ToList()
                };

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var (ok, message, _) = await _sets.SaveAsync(edit, userId);
                if (ok)
                {
                    if (existing?.ImageUrl != photoUrl) _images.DeleteIfOurs(existing?.ImageUrl);
                    TempData["StatusMessage"] = status switch
                    {
                        SetMenuStatus.Active when existing?.Status != SetMenuStatus.Active => $"{edit.Name} is now on offer.",
                        SetMenuStatus.Hidden when existing?.Status != SetMenuStatus.Hidden => $"{edit.Name} is hidden from guests.",
                        SetMenuStatus.Draft => $"{edit.Name} is saved as a draft.",
                        _ => message
                    };
                    return RedirectToPage("/" + Area + "/SetMenus");
                }
                _images.DeleteIfOurs(photoUrl is not null && photoUrl != existing?.ImageUrl ? photoUrl : null);
                problem = message;
            }
        }

        // Show the builder again with exactly what was typed, so nothing has to be entered twice.
        Problem = problem;
        StateJson = Payload is { Length: > 0 } && IsJsonObject(Payload) ? ScriptSafe(Payload) : JsonSerializer.Serialize(EmptyState(), Json);
        await LoadItemsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!Id.HasValue) return RedirectToPage("/" + Area + "/SetMenus");
        var (ok, message) = await _sets.DeleteAsync(Id.Value);
        TempData[ok ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage("/" + Area + "/SetMenus");
    }

    // ---------------------------------------------------------------------------------------------------------------

    private class StateDto
    {
        public string? Name { get; set; }
        public string? Tagline { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public List<int> Days { get; set; } = new();
        public bool Lunch { get; set; }
        public bool Dinner { get; set; }
        public List<CourseDto> Courses { get; set; } = new();
    }

    private class CourseDto
    {
        public string? Name { get; set; }
        public int Picks { get; set; } = 1;
        public List<DishDto> Dishes { get; set; } = new();
    }

    private class DishDto
    {
        public int Id { get; set; }
        public decimal Supplement { get; set; }
    }

    private string? Read(out StateDto? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(Payload) || Payload.Length > 200_000) return "The menu could not be read. Reload the page and try again.";
        try { state = JsonSerializer.Deserialize<StateDto>(Payload, Json); }
        catch (JsonException) { return "The menu could not be read. Reload the page and try again."; }
        if (state == null) return "The menu could not be read. Reload the page and try again.";

        if (string.IsNullOrWhiteSpace(state.Name)) return "Give the set menu a name.";
        if (state.Name.Trim().Length > 80) return "Keep the name under 80 characters.";
        if (state.Tagline is { Length: > 120 }) return "Keep the short line under 120 characters.";
        if (state.Description is { Length: > 500 }) return "Keep the description under 500 characters.";
        if (state.Price <= 0 || state.Price > 100_000) return "Enter the price per guest.";
        if (state.Courses.Count > 12) return "A set menu can have up to 12 courses.";
        if (state.Courses.Any(c => c.Name is { Length: > 40 })) return "Keep each course name under 40 characters.";
        if (state.Courses.Any(c => c.Picks is < 0 or > 5)) return "A guest can pick up to five dishes in a course.";
        if (state.Courses.Any(c => c.Dishes.Any(d => d.Supplement is < 0 or > 10_000))) return "An extra charge cannot be negative.";
        return null;
    }

    /// 
    /// The posted text is written back into a &lt;script type="application/json"&gt; block. A "&lt;" can only appear inside a JSON string,
    /// where "\u003C" means the same thing, so swapping it stops a typed "&lt;/script&gt;" from ending the block early.
    /// 
    private static string ScriptSafe(string json) => json.Replace("<", "\\u003C");

    private static bool IsJsonObject(string s)
    {
        try { using var d = JsonDocument.Parse(s); return d.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }

    private static object EmptyState() => new
    {
        id = (int?)null, name = "", tagline = "", price = (decimal?)null, description = "",
        days = new[] { 1, 2, 3, 4, 5, 6, 0 }, lunch = false, dinner = true,
        courses = new[]
        {
            new { name = "Starter", picks = 1, dishes = Array.Empty<object>() },
            new { name = "Main", picks = 1, dishes = Array.Empty<object>() }
        }
    };

    private static object StateOf(SetMenu m) => new
    {
        id = (int?)m.Id, name = m.Name, tagline = m.Tagline ?? "", price = (decimal?)m.PricePerGuest, description = m.Description ?? "",
        days = new[] { 1, 2, 3, 4, 5, 6, 0 }.Where(d => m.OffersOn((DayOfWeek)d)).ToArray(),
        lunch = m.ServedAtLunch, dinner = m.ServedAtDinner,
        courses = m.Courses.OrderBy(c => c.SortOrder).Select(c => new
        {
            name = c.Name, picks = c.Picks,
            dishes = c.Dishes.OrderBy(d => d.SortOrder).Select(d => new { id = d.MenuItemId, supplement = d.Supplement }).ToList()
        }).ToList()
    };

    private async Task LoadItemsAsync()
    {
        var items = await _context.MenuItems.AsNoTracking().Include(m => m.MenuCategory)
            .OrderBy(m => m.MenuCategory!.DisplayOrder).ThenBy(m => m.Name).ToListAsync();
        var options = items.Select(m =>
        {
            // a wine sold by the glass is compared by its glass price: that is what a set menu serves
            var price = m.MenuCategory?.IsWine == true && m.GlassPrice.HasValue ? m.GlassPrice.Value : m.Price;
            var pic = Ui.Pic(m.ImageUrl, m.Name, m.MenuCategory?.Name, "round", decorative: true).ToString() ?? "";
            return new SetMenuItemOption(m.Id, m.Name, m.MenuCategory?.Name ?? "Other", price, !string.IsNullOrWhiteSpace(m.ImageUrl), m.IsAvailable, pic);
        }).ToList();
        ItemsJson = JsonSerializer.Serialize(options, Json);
    }
}
