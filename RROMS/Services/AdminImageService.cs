namespace RROMS.Services;

public interface IAdminImageService
{
    /// 
    /// Validates and stores an image an administrator uploaded for one of the site's content areas
    /// ("menu", "dining", "announcements", "collaborations"); returns the site-relative URL to save on the record.
    /// 
    Task<(bool Success, string Message, string? Url)> SaveAsync(IFormFile file, string area);

    /// Deletes a previously uploaded image, but only if it lives in one of our own upload folders.
    void DeleteIfOurs(string? url);

    /// 
    /// Decides what image a record should end up with from an admin form that offers both a file upload and an
    /// address box: an uploaded file wins; otherwise the (trimmed, validated) address is kept; an empty box clears
    /// the image. Edit forms pre-fill the address box with the current image, so "leave it alone" keeps it.
    /// 
    Task<(bool Success, string Message, string? Url)> ResolveAsync(IFormFile? file, string? pastedUrl, string area);
}

/// 
/// Images administrators attach to menu items, dining areas, announcements and collaborations
/// (brief 7 "Image handling"). Same rules as profile pictures and review photos: real file signature
/// check, size cap, generated filename (the browser's filename is never used), fixed folder per area.
/// 
public class AdminImageService : IAdminImageService
{
    public const long MaxBytes = 4 * 1024 * 1024;

    // The area comes from our own code, never from the browser - but it is still checked against a fixed list.
    private static readonly string[] Areas = { "menu", "dining", "announcements", "collaborations" };
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IWebHostEnvironment _env;

    public AdminImageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<(bool Success, string Message, string? Url)> SaveAsync(IFormFile file, string area)
    {
        if (!Areas.Contains(area))
            return (false, "Unknown upload area.", null);

        if (file == null || file.Length == 0)
            return (false, "Please choose an image to upload.", null);

        if (file.Length > MaxBytes)
            return (false, "That image is too large. Please choose one under 4 MB.", null);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return (false, "Please upload a JPG, PNG or WebP image.", null);

        var header = new byte[12];
        await using (var peek = file.OpenReadStream())
        {
            var read = await peek.ReadAsync(header.AsMemory(0, header.Length));
            if (read < header.Length || !ProfileImageService.MatchesSignature(header, extension))
                return (false, "That file doesn't look like a valid image.", null);
        }

        var folder = Path.Combine(_env.WebRootPath, "uploads", area);
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using (var output = File.Create(Path.Combine(folder, fileName)))
        {
            await file.CopyToAsync(output);
        }

        return (true, "Image saved.", $"/uploads/{area}/{fileName}");
    }

    public async Task<(bool Success, string Message, string? Url)> ResolveAsync(IFormFile? file, string? pastedUrl, string area)
    {
        if (file != null && file.Length > 0)
            return await SaveAsync(file, area);

        var trimmed = string.IsNullOrWhiteSpace(pastedUrl) ? null : pastedUrl.Trim();
        if (trimmed == null)
            return (true, string.Empty, null);

        if (trimmed.Length > 300 || !UrlSafety.IsAcceptableImageUrl(trimmed))
            return (false, "The image address must be an https link, or an image uploaded here.", null);

        return (true, string.Empty, trimmed);
    }

    public void DeleteIfOurs(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;

        foreach (var area in Areas)
        {
            var prefix = $"/uploads/{area}/";
            if (!url.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var fileName = Path.GetFileName(url); // strips any directory parts
            var fullPath = Path.Combine(_env.WebRootPath, "uploads", area, fileName);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            return;
        }
    }
}
