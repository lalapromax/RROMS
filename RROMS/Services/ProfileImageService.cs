namespace RROMS.Services;

public interface IProfileImageService
{
    /// Validates and stores an uploaded picture; returns the site-relative URL to save on the user.
    Task<(bool Success, string Message, string? Url)> SaveAsync(IFormFile file, string userId);

    /// Deletes a previously stored picture, but only if it lives in our own uploads folder.
    void DeleteIfOurs(string? url);
}

/// 
/// Profile picture storage (brief 6.E). Uploads are untrusted input, so this checks the file's
/// real signature (not just its extension or the browser-supplied content type), caps the size,
/// and ignores the original filename entirely - the stored name is generated here, which also
/// rules out path traversal and overwriting someone else's picture.
/// 
public class ProfileImageService : IProfileImageService
{
    public const string UploadsUrlFolder = "/uploads/profiles/";
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IWebHostEnvironment _env;

    public ProfileImageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<(bool Success, string Message, string? Url)> SaveAsync(IFormFile file, string userId)
    {
        if (file == null || file.Length == 0)
            return (false, "Please choose a picture to upload.", null);

        if (file.Length > MaxBytes)
            return (false, "That picture is too large. Please choose one under 2 MB.", null);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return (false, "Please upload a JPG, PNG or WebP picture.", null);

        var header = new byte[12];
        await using (var peek = file.OpenReadStream())
        {
            var read = await peek.ReadAsync(header.AsMemory(0, header.Length));
            if (read < header.Length || !MatchesSignature(header, extension))
                return (false, "That file doesn't look like a valid picture.", null);
        }

        var folder = Path.Combine(_env.WebRootPath, "uploads", "profiles");
        Directory.CreateDirectory(folder);

        var fileName = $"{userId}-{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);
        await using (var output = File.Create(fullPath))
        {
            await file.CopyToAsync(output);
        }

        return (true, "Picture saved.", UploadsUrlFolder + fileName);
    }

    public void DeleteIfOurs(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(UploadsUrlFolder, StringComparison.Ordinal))
            return;

        var fileName = Path.GetFileName(url); // strips any directory parts
        var fullPath = Path.Combine(_env.WebRootPath, "uploads", "profiles", fileName);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    // internal (not private) so ReviewImageService applies the identical signature check.
    internal static bool MatchesSignature(byte[] h, string extension) => extension switch
    {
        ".jpg" or ".jpeg" => h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF,
        ".png" => h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47,
        ".webp" => h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P',
        _ => false
    };
}
