namespace RROMS.ViewModels;

/// Everything the _Avatar partial needs: the stored picture (if any), the name for the
/// default initials avatar, and the rendered size in pixels.
public record AvatarVM(string? ImagePath, string? FullName, int Size = 32);
