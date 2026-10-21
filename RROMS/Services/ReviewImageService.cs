namespace RROMS.Services;

public interface IReviewImageService
{
    /// Validates and stores one review photo; returns the site-relative URL to save on the review.
    Task<(bool Success, string Message, string? Url)> SaveAsync(IFormFile file, string customerId);

    /// Deletes a stored review photo, but only if it lives in our own review-uploads folder.
    void DeleteIfOurs(string? url);
}

/// 
/// Review photos (brief 6.H / "Image handling"). Same rules as profile pictures - real file
/// signature check, size cap, generated filename that ignores whatever name the browser sent -
/// stored in their own folder.
/// 
public class ReviewImageService : IReviewImageService
{
    public const string UploadsUrlFolder = "/uploads/reviews/";
    public const long MaxBytes = 3 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IWebHostEnvironment _env;

    public ReviewImageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<(bool Success, string Message, string? Url)> SaveAsync(IFormFile file, string customerId)
    {
        if (file == null || file.Length == 0)
            return (false, "One of the selected photos was empty.", null);

        if (file.Length > MaxBytes)
            return (false, "A photo is too large. Please keep each photo under 3 MB.", null);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return (false, "Photos must be JPG, PNG or WebP files.", null);

        var header = new byte[12];
        await using (var peek = file.OpenReadStream())
        {
            var read = await peek.ReadAsync(header.AsMemory(0, header.Length));
            if (read < header.Length || !ProfileImageService.MatchesSignature(header, extension))
                return (false, "One of the files doesn't look like a valid picture.", null);
        }

        var folder = Path.Combine(_env.WebRootPath, "uploads", "reviews");
        Directory.CreateDirectory(folder);

        var fileName = $"{customerId}-{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);
        await using (var output = File.Create(fullPath))
        {
            await file.CopyToAsync(output);
        }

        return (true, "Photo saved.", UploadsUrlFolder + fileName);
    }

    public void DeleteIfOurs(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(UploadsUrlFolder, StringComparison.Ordinal))
            return;

        var fileName = Path.GetFileName(url); // strips any directory parts
        var fullPath = Path.Combine(_env.WebRootPath, "uploads", "reviews", fileName);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
