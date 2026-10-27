using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;
using RROMS.ViewModels;

namespace RROMS.Pages.Account;

/// <summary>
/// "Your profile" for every signed-in person (guest, staff or administrator). Guests also get birthday, dietary needs,
/// notification choices and the option to close their account; the team's accounts are managed by an administrator.
/// Every handler works on the signed-in user only, never on an id from the form.
/// </summary>
public class ProfileModel : PageModel
{
    private const string GoogleProvider = "Google";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IProfileImageService _profileImages;
    private readonly IGoogleAuthService _googleAuth;
    private readonly IRewardService _rewardService;
    private readonly INotificationService _notifications;
    private readonly IEmailService _email;
    private readonly ApplicationDbContext _context;

    public ProfileModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IProfileImageService profileImages,
        IGoogleAuthService googleAuth,
        IRewardService rewardService,
        INotificationService notifications,
        IEmailService email,
        ApplicationDbContext context)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _profileImages = profileImages;
        _googleAuth = googleAuth;
        _rewardService = rewardService;
        _notifications = notifications;
        _email = email;
        _context = context;
    }

    public string Email { get; set; } = string.Empty;
    public string? ProfileImagePath { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime Joined { get; set; }
    public bool IsCustomer { get; set; }
    public bool IsAdmin { get; set; }

    /// <summary>Google-created accounts start without a password; they can add one below.</summary>
    public bool HasPassword { get; set; }
    public bool GoogleLinked { get; set; }
    public string? GoogleEmail { get; set; }
    public bool GoogleEnabled => _googleAuth.IsConfigured;
    public string GoogleClientId => _googleAuth.ClientId;

    /// <summary>Only customers have a rewards account.</summary>
    public RewardStanding? Rewards { get; set; }

    /// <summary>Allergy words typed in the free-text box that the menu filter cannot match (shown so the guest tells the staff).</summary>
    public List<string> UnrecognisedAllergies { get; set; } = new();

    public NoticePrefsVM Prefs { get; set; } = new(true, true, false, true);

    /// <summary>Set when closing the account failed, so the page opens the confirmation again with the reason.</summary>
    public string? DeleteError { get; set; }

    [BindProperty] public ProfileInputModel ProfileInput { get; set; } = new();
    [BindProperty] public DietInputModel Diet { get; set; } = new();
    [BindProperty] public PasswordInputModel PasswordInput { get; set; } = new();
    [BindProperty] public SetPasswordInputModel SetPasswordInput { get; set; } = new();
    [BindProperty] public DeleteInputModel DeleteInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        await LoadViewDataAsync(user);
        ProfileInput = BuildProfileInput(user);
        Diet = BuildDiet(user);
        return Page();
    }

    private async Task LoadViewDataAsync(ApplicationUser user)
    {
        Email = user.Email ?? string.Empty;
        DisplayName = user.FullName;
        ProfileImagePath = user.ProfileImagePath;
        Joined = user.DateJoined.ToLocalTime();
        IsCustomer = User.IsInRole(Roles.Customer);
        IsAdmin = User.IsInRole(Roles.Administrator);
        HasPassword = await _userManager.HasPasswordAsync(user);
        GoogleLinked = (await _userManager.GetLoginsAsync(user)).Any(l => l.LoginProvider == GoogleProvider);
        GoogleEmail = GoogleLinked ? user.Email : null;
        Prefs = new NoticePrefsVM(user.NoticeAnnouncements, user.NoticeRewards, user.NoticeOffers, user.NoticeByEmail);
        UnrecognisedAllergies = DietaryRules.ProfileFor(user).UnrecognisedAllergies.ToList();

        if (IsCustomer)
            Rewards = await _rewardService.GetStandingAsync(user.Id);
    }

    private static ProfileInputModel BuildProfileInput(ApplicationUser user) => new()
    {
        FullName = user.FullName,
        Phone = user.PhoneNumber,
        Email = user.Email ?? string.Empty,
        BirthMonth = user.BirthMonth,
        BirthDay = user.BirthDay
    };

    private static DietInputModel BuildDiet(ApplicationUser user)
    {
        var (chips, other) = DietaryRules.SplitAllergies(user.Allergies);
        return new DietInputModel
        {
            Needs = DietaryRules.ParsePreferences(user.DietaryPreference),
            Allergies = chips,
            Other = string.IsNullOrWhiteSpace(other) ? null : other
        };
    }

    // ---------------- Personal details ----------------

    public async Task<IActionResult> OnPostUpdateProfileAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var isCustomer = User.IsInRole(Roles.Customer);

        ModelState.Clear();
        TryValidateModel(ProfileInput, nameof(ProfileInput));

        // Birthday is month and day only, and both or neither. 2000 is a leap year, so 29 February is allowed.
        int? month = null, day = null;
        if (isCustomer)
        {
            if (ProfileInput.BirthMonth.HasValue != ProfileInput.BirthDay.HasValue)
                ModelState.AddModelError("ProfileInput.BirthDay", "Choose both a month and a day, or leave both empty.");
            else if (ProfileInput.BirthMonth is int m && ProfileInput.BirthDay is int d)
            {
                if (m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(2000, m))
                    ModelState.AddModelError("ProfileInput.BirthDay", "That date does not exist.");
                else { month = m; day = d; }
            }
        }

        var newEmail = (ProfileInput.Email ?? string.Empty).Trim();
        var emailChanged = ModelState.GetValidationState("ProfileInput.Email") != Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid
                           && !string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase);
        if (emailChanged)
        {
            var taken = await _userManager.FindByEmailAsync(newEmail);
            if (taken != null && taken.Id != user.Id)
                ModelState.AddModelError("ProfileInput.Email", "That email already belongs to another account.");
            else if (!_email.IsConfigured)
                ModelState.AddModelError("ProfileInput.Email", "This site can't send the confirmation email yet, so the address can't be changed here. Ask the restaurant to change it for you.");
        }

        if (!ModelState.IsValid)
        {
            await LoadViewDataAsync(user);
            Diet = BuildDiet(user);
            return Page();
        }

        if (emailChanged)
        {
            // The address only changes once the link sent to the new one is opened (see Account/ConfirmEmail).
            var token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Page("/Account/ConfirmEmail", null, new { userId = user.Id, email = newEmail, code = encoded }, Request.Scheme)!;
            var (sent, message) = await _email.SendAsync(newEmail, $"Confirm your new {Brand.Name} email",
                $"Hello {ProfileInput.FullName.Trim()},\n\nYou asked to use this address to sign in to {Brand.Name}. To confirm it, open this link:\n\n{link}\n\nUntil you do, your old address keeps working. If this wasn't you, ignore this email and nothing changes.\n\n{Brand.Name}");
            if (!sent)
            {
                ModelState.AddModelError("ProfileInput.Email", "We couldn't send the confirmation email: " + message);
                await LoadViewDataAsync(user);
                Diet = BuildDiet(user);
                return Page();
            }
        }

        user.FullName = ProfileInput.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(ProfileInput.Phone) ? null : ProfileInput.Phone.Trim();
        if (isCustomer) { user.BirthMonth = month; user.BirthDay = day; }
        await _userManager.UpdateAsync(user);

        TempData["StatusMessage"] = emailChanged
            ? $"Saved. We sent a link to {newEmail}. Your sign-in email changes once you open it."
            : "Your details are saved.";
        return RedirectToPage();
    }

    // ---------------- Dietary needs and allergies ----------------

    public async Task<IActionResult> OnPostSaveDietaryAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!User.IsInRole(Roles.Customer)) return Forbid();

        ModelState.Clear();
        if (!TryValidateModel(Diet, nameof(Diet)))
        {
            await LoadViewDataAsync(user);
            ProfileInput = BuildProfileInput(user);
            return Page();
        }

        // Anything an older account has stored that is not one of the six chips stays, so saving never drops it silently.
        var keep = DietaryRules.ParsePreferences(user.DietaryPreference)
            .Where(p => !DietaryRules.NeedChips.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
        var chosen = (DietaryRules.JoinNeeds(Diet.Needs) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        var all = chosen.Concat(keep.Where(k => Diet.Needs.Contains(k, StringComparer.OrdinalIgnoreCase))).ToList();
        user.DietaryPreference = all.Count == 0 ? null : string.Join(",", all);
        user.Allergies = DietaryRules.JoinAllergies(Diet.Allergies, Diet.Other);

        await _userManager.UpdateAsync(user);
        TempData["StatusMessage"] = "Dietary details saved. The kitchen sees them with every booking.";
        return RedirectToPage(null, null, null, "dietary");
    }

    // ---------------- Notification choices (guests) ----------------

    public async Task<IActionResult> OnPostPrefsAsync(bool announcements, bool rewards, bool offers, bool email)
    {
        if (!User.IsInRole(Roles.Customer)) return Forbid();
        await _notifications.SavePreferencesAsync(_userManager.GetUserId(User)!, announcements, rewards, offers, email);
        TempData["StatusMessage"] = "Saved. We will reach you the way you chose.";
        return RedirectToPage(null, null, null, "notices");
    }

    // ---------------- Profile picture ----------------

    public async Task<IActionResult> OnPostUploadPictureAsync(IFormFile? picture)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (picture == null)
        {
            TempData["ErrorMessage"] = "Please choose a picture to upload.";
            return RedirectToPage();
        }

        var (success, message, url) = await _profileImages.SaveAsync(picture, user.Id);
        if (!success || url == null)
        {
            TempData["ErrorMessage"] = message;
            return RedirectToPage();
        }

        var previous = user.ProfileImagePath;
        user.ProfileImagePath = url;
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            // Don't leave an orphan file behind if the database refused the change.
            _profileImages.DeleteIfOurs(url);
            TempData["ErrorMessage"] = "We couldn't save your picture. Please try again.";
            return RedirectToPage();
        }

        _profileImages.DeleteIfOurs(previous);
        TempData["StatusMessage"] = "Your photo is updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemovePictureAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var previous = user.ProfileImagePath;
        user.ProfileImagePath = null;
        await _userManager.UpdateAsync(user);
        _profileImages.DeleteIfOurs(previous);

        TempData["StatusMessage"] = "Your photo is removed. We will show your initials instead.";
        return RedirectToPage();
    }

    // ---------------- Passwords ----------------

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        ModelState.Clear();
        if (!TryValidateModel(PasswordInput, nameof(PasswordInput)))
            return await BackToFormsAsync(user);

        var result = await _userManager.ChangePasswordAsync(user, PasswordInput.CurrentPassword, PasswordInput.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("PasswordInput.NewPassword", error.Description);
            return await BackToFormsAsync(user);
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["StatusMessage"] = "Your password is changed.";
        return RedirectToPage(null, null, null, "signin");
    }

    /// <summary>For accounts created with Google, which have no password yet.</summary>
    public async Task<IActionResult> OnPostSetPasswordAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (await _userManager.HasPasswordAsync(user))
        {
            TempData["ErrorMessage"] = "Your account already has a password. Use \"Change password\" instead.";
            return RedirectToPage(null, null, null, "signin");
        }

        ModelState.Clear();
        if (!TryValidateModel(SetPasswordInput, nameof(SetPasswordInput)))
            return await BackToFormsAsync(user);

        var result = await _userManager.AddPasswordAsync(user, SetPasswordInput.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("SetPasswordInput.NewPassword", error.Description);
            return await BackToFormsAsync(user);
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["StatusMessage"] = "Password set. You can now sign in with your email and password too.";
        return RedirectToPage(null, null, null, "signin");
    }

    private async Task<IActionResult> BackToFormsAsync(ApplicationUser user)
    {
        await LoadViewDataAsync(user);
        ProfileInput = BuildProfileInput(user);
        Diet = BuildDiet(user);
        return Page();
    }

    // ---------------- Google account link ----------------

    /// <summary>
    /// Connects the signed-in account to a Google identity. Safe to allow here (unlike on the
    /// login page) because the person is already authenticated as this account, and the Google
    /// token is still fully verified on the server first.
    /// </summary>
    public async Task<IActionResult> OnPostLinkGoogleAsync(string? credential)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (verified, message, identity) = await _googleAuth.VerifyAsync(credential ?? string.Empty, HttpContext.Session);
        if (!verified || identity == null)
            return Fail(message);

        var existingOwner = await _userManager.FindByLoginAsync(GoogleProvider, identity.Subject);
        if (existingOwner != null && existingOwner.Id != user.Id)
            return Fail("That Google account is already connected to a different account.");

        if (existingOwner == null)
        {
            // A user holds at most one Google link.
            var current = (await _userManager.GetLoginsAsync(user)).FirstOrDefault(l => l.LoginProvider == GoogleProvider);
            if (current != null)
                return Fail("A Google account is already connected. Disconnect it first to connect a different one.");

            var added = await _userManager.AddLoginAsync(user, new UserLoginInfo(GoogleProvider, identity.Subject, GoogleProvider));
            if (!added.Succeeded)
                return Fail("We couldn't connect your Google account: " + string.Join(" ", added.Errors.Select(e => e.Description)));
        }

        TempData["StatusMessage"] = "Google account connected.";
        return new JsonResult(new { success = true, redirectUrl = Url.Page("/Account/Profile") + "#signin" });
    }

    public async Task<IActionResult> OnPostUnlinkGoogleAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        // Without a password, Google is the only way into this account - removing it would lock them out.
        if (!await _userManager.HasPasswordAsync(user))
        {
            TempData["ErrorMessage"] = "Set a password first. Otherwise you would have no way to sign in once Google is disconnected.";
            return RedirectToPage(null, null, null, "signin");
        }

        var login = (await _userManager.GetLoginsAsync(user)).FirstOrDefault(l => l.LoginProvider == GoogleProvider);
        if (login != null)
            await _userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);

        TempData["StatusMessage"] = "Google is disconnected.";
        return RedirectToPage(null, null, null, "signin");
    }

    private static JsonResult Fail(string message) =>
        new(new { success = false, message }) { StatusCode = StatusCodes.Status400BadRequest };

    // ---------------- Closing the account (guests only) ----------------

    /// Doesn't actually delete account, because the restaurant needs to keep the history of orders and payments for accounting.
    /// Instead, it removes all personal details and cancels any upcoming bookings.

    public async Task<IActionResult> OnPostDeleteAccountAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        // Staff and administrator accounts are ended by an administrator (Team), never from here.
        if (!User.IsInRole(Roles.Customer)) return Forbid();

        ModelState.Clear();
        var hasPassword = await _userManager.HasPasswordAsync(user);
        var proven = hasPassword
            ? !string.IsNullOrEmpty(DeleteInput.Password) && await _userManager.CheckPasswordAsync(user, DeleteInput.Password)
            : string.Equals(DeleteInput.Confirm?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase);
        if (!proven)
        {
            DeleteError = hasPassword ? "That password is not right, so nothing was deleted." : "Type DELETE in capitals to confirm.";
            return await BackToFormsAsync(user);
        }

        var openOrders = await _context.Orders.AnyAsync(o => o.CustomerId == user.Id && o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled);
        var seatedNow = await _context.Reservations.AnyAsync(r => r.CustomerId == user.Id && r.Status == ReservationStatus.Seated);
        if (openOrders || seatedNow)
        {
            DeleteError = "You have an order in progress or a table at the restaurant right now. Please close your account once your visit is finished.";
            return await BackToFormsAsync(user);
        }

        var now = DateTime.Now;
        var reservations = await _context.Reservations.Where(r => r.CustomerId == user.Id).ToListAsync();
        foreach (var r in reservations)
        {
            if ((r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) && r.ReservationDateTime > now)
            {
                r.Status = ReservationStatus.Cancelled;
                r.CancellationReason = "The guest closed their account.";
            }
            r.ContactName = null;
            r.ContactPhone = null;
            r.SpecialRequests = null;
        }

        _context.RewardLedger.RemoveRange(_context.RewardLedger.Where(l => l.CustomerId == user.Id));
        _context.RewardClaims.RemoveRange(_context.RewardClaims.Where(c => c.CustomerId == user.Id));
        _context.Notifications.RemoveRange(_context.Notifications.Where(n => n.UserId == user.Id));
        _context.AnnouncementRecipients.RemoveRange(_context.AnnouncementRecipients.Where(a => a.UserId == user.Id));
        _context.WaitlistEntries.RemoveRange(_context.WaitlistEntries.Where(w => w.CustomerId == user.Id));
        foreach (var f in await _context.Feedbacks.Where(f => f.CustomerId == user.Id).ToListAsync())
            f.ShowFirstName = false;
        await _context.SaveChangesAsync();

        foreach (var login in await _userManager.GetLoginsAsync(user))
            await _userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        if (hasPassword) await _userManager.RemovePasswordAsync(user);

        _profileImages.DeleteIfOurs(user.ProfileImagePath);
        var placeholder = $"closed-{user.Id}@closed.invalid";
        user.FullName = "Former guest";
        user.Address = null;
        user.DietaryPreference = null;
        user.Allergies = null;
        user.ProfileImagePath = null;
        user.BirthMonth = null;
        user.BirthDay = null;
        user.PhoneNumber = null;
        user.ShowOnLeaderboard = false;
        user.IsActive = false;
        user.NoticeAnnouncements = user.NoticeRewards = user.NoticeOffers = user.NoticeByEmail = false;
        await _userManager.UpdateAsync(user);
        await _userManager.SetUserNameAsync(user, placeholder);
        await _userManager.SetEmailAsync(user, placeholder);
        await _userManager.UpdateSecurityStampAsync(user);

        await _signInManager.SignOutAsync();
        TempData["StatusMessage"] = "Your account is closed and your details are removed. Thank you for dining with us.";
        return RedirectToPage("/Index");
    }

    // ---------------- Input models ----------------

    public class ProfileInputModel
    {
        [Required(ErrorMessage = "Tell us your name."), StringLength(100), Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Phone(ErrorMessage = "That does not look like a phone number."), StringLength(30), Display(Name = "Phone")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = "That does not look like an email address."), StringLength(256), Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        public int? BirthMonth { get; set; }
        public int? BirthDay { get; set; }
    }

    public class DietInputModel
    {
        public List<string> Needs { get; set; } = new();
        public List<string> Allergies { get; set; } = new();

        [StringLength(150)]
        public string? Other { get; set; }
    }

    public class PasswordInputModel
    {
        [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password), Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose a new password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters."), DataType(DataType.Password), Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "The two passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class SetPasswordInputModel
    {
        [Required(ErrorMessage = "Choose a password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters."), DataType(DataType.Password), Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "The two passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class DeleteInputModel
    {
        [DataType(DataType.Password)]
        public string? Password { get; set; }
        public string? Confirm { get; set; }
    }
}
