using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// <summary>
/// Manage the home page's collaborations strip (brief 6.J). Administrator-only through the
/// /Admin folder policy in Program.cs. Everything an administrator types or uploads is validated here,
/// on the server: link schemes, social-post addresses, image type/size. A refused save re-opens the editor with
/// what was typed, so nothing has to be entered again.
/// </summary>
[RequestSizeLimit(6 * 1024 * 1024)]
public class CollaborationsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IAdminImageService _images;
    private readonly IAuditService _audit;
    private readonly UserManager<ApplicationUser> _userManager;

    public CollaborationsModel(ApplicationDbContext context, IAdminImageService images, IAuditService audit, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _images = images;
        _audit = audit;
        _userManager = userManager;
    }

    [BindProperty(Name = "F")] public CollabInput F { get; set; } = new();
    [BindProperty(Name = "ImageFile")] public IFormFile? ImageFile { get; set; }

    public List<Collaboration> Items { get; set; } = new();
    public bool OpenForm { get; private set; }
    public string? FormError { get; private set; }
    /// <summary>The picture the entry being edited has now, read from the database (never from the form).</summary>
    public string? CurrentImage { get; private set; }

    public class CollabInput
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Summary { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? SocialUrl { get; set; }
        public int? Year { get; set; }
        public bool IsOngoing { get; set; }
        public bool IsPublished { get; set; } = true;
        /// <summary>An https address for the picture, when it is not uploaded.</summary>
        public string? ImageUrl { get; set; }
        public bool RemoveImage { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        Items = await _context.Collaborations.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id).ToListAsync();

    /// <summary>Add (id 0) or edit (id set) one entry.</summary>
    public async Task<IActionResult> OnPostSaveAsync()
    {
        var name = F.Name?.Trim();
        var summary = string.IsNullOrWhiteSpace(F.Summary) ? null : F.Summary.Trim();
        var websiteUrl = string.IsNullOrWhiteSpace(F.WebsiteUrl) ? null : F.WebsiteUrl.Trim();
        var socialUrl = string.IsNullOrWhiteSpace(F.SocialUrl) ? null : F.SocialUrl.Trim();
        var imageUrl = string.IsNullOrWhiteSpace(F.ImageUrl) ? null : F.ImageUrl.Trim();

        // ---- validation ----
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return await Reopen("Please enter the partner's name (up to 100 characters).");
        if (summary != null && summary.Length > 200)
            return await Reopen("Keep the line about what you did together under 200 characters.");
        if (websiteUrl != null && (websiteUrl.Length > 300 || !UrlSafety.IsHttpUrl(websiteUrl)))
            return await Reopen("The website must be a full web address starting with http:// or https://.");
        if (socialUrl != null && (socialUrl.Length > 300 || !SocialEmbed.TryParse(socialUrl, out _)))
            return await Reopen("The social link must be an https Instagram reel or post, or a YouTube video link.");
        if (imageUrl != null && (imageUrl.Length > 300 || !UrlSafety.IsAcceptableImageUrl(imageUrl)))
            return await Reopen("The picture address must be an https link (or a picture you uploaded here).");
        if (F.Year.HasValue && (F.Year < 1990 || F.Year > 2100))
            return await Reopen("Please enter a sensible year.");

        Collaboration entry;
        var isEdit = F.Id > 0;
        if (isEdit)
        {
            var existing = await _context.Collaborations.FindAsync(F.Id);
            if (existing == null)
            {
                TempData["ErrorMessage"] = "That collaboration no longer exists.";
                return RedirectToPage();
            }
            entry = existing;
        }
        else
        {
            entry = new Collaboration { CreatedAt = DateTime.UtcNow };
            entry.DisplayOrder = (await _context.Collaborations.MaxAsync(c => (int?)c.DisplayOrder) ?? 0) + 1;
        }

        // ---- picture: an uploaded file wins over a pasted address; "take it off" clears it ----
        var previousImage = entry.ImageUrl;
        string? newImage = F.RemoveImage ? null : (imageUrl ?? previousImage);
        if (ImageFile != null && ImageFile.Length > 0)
        {
            var (ok, message, url) = await _images.SaveAsync(ImageFile, "collaborations");
            if (!ok || url == null) return await Reopen(message);
            newImage = url;
        }

        entry.Name = name;
        entry.Summary = summary;
        entry.WebsiteUrl = websiteUrl;
        entry.SocialUrl = socialUrl;
        entry.Year = F.Year;
        entry.IsOngoing = F.IsOngoing;
        entry.IsPublished = F.IsPublished;
        entry.ImageUrl = newImage;
        if (!isEdit) _context.Collaborations.Add(entry);

        await _context.SaveChangesAsync();

        if (previousImage != newImage)
            _images.DeleteIfOurs(previousImage);

        await LogAsync(isEdit ? "UpdateCollaboration" : "CreateCollaboration", name);
        TempData["StatusMessage"] = isEdit ? "Collaboration updated." : "Collaboration added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTogglePublishedAsync(int id)
    {
        var entry = await _context.Collaborations.FindAsync(id);
        if (entry == null) return Fail("That collaboration no longer exists.");

        entry.IsPublished = !entry.IsPublished;
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = entry.IsPublished ? $"{entry.Name} is now on the home page." : $"{entry.Name} is hidden from the home page.";
        return RedirectToPage();
    }

    /// <summary>Move one entry up or down. Orders are renumbered 1..n first so ties can never make a move do nothing.</summary>
    public async Task<IActionResult> OnPostMoveAsync(int id, string direction)
    {
        var all = await _context.Collaborations.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id).ToListAsync();
        var index = all.FindIndex(c => c.Id == id);
        if (index < 0) return Fail("That collaboration no longer exists.");

        var swapWith = direction == "up" ? index - 1 : direction == "down" ? index + 1 : index;
        if (swapWith >= 0 && swapWith < all.Count && swapWith != index)
            (all[index], all[swapWith]) = (all[swapWith], all[index]);

        for (var i = 0; i < all.Count; i++)
            all[i].DisplayOrder = i + 1;

        await _context.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var entry = await _context.Collaborations.FindAsync(id);
        if (entry != null)
        {
            _context.Collaborations.Remove(entry);
            await _context.SaveChangesAsync();
            _images.DeleteIfOurs(entry.ImageUrl);
            await LogAsync("DeleteCollaboration", entry.Name);
            TempData["StatusMessage"] = "Collaboration removed.";
        }
        return RedirectToPage();
    }

    private IActionResult Fail(string message)
    {
        TempData["ErrorMessage"] = message;
        return RedirectToPage();
    }

    private async Task<IActionResult> Reopen(string problem)
    {
        await LoadAsync();
        OpenForm = true;
        FormError = problem;
        if (F.Id > 0)
            CurrentImage = await _context.Collaborations.AsNoTracking().Where(c => c.Id == F.Id).Select(c => c.ImageUrl).FirstOrDefaultAsync();
        return Page();
    }

    private async Task LogAsync(string action, string name)
    {
        var admin = await _userManager.GetUserAsync(User);
        if (admin != null)
            await _audit.LogAsync(admin.Id, admin.FullName, action, name);
    }
}
