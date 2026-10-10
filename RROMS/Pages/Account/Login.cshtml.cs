using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    public const string GoogleProvider = "Google";

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IGoogleAuthService _googleAuth;

    public LoginModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IGoogleAuthService googleAuth)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _googleAuth = googleAuth;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool GoogleEnabled => _googleAuth.IsConfigured;
    public string GoogleClientId => _googleAuth.ClientId;

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl = SafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return Page();
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated. Please contact the restaurant.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(user.UserName!, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return LocalRedirect(await LandingUrlAsync(user, returnUrl));

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "This account is temporarily locked due to repeated failed attempts. Please try again later.");
        else
            ModelState.AddModelError(string.Empty, "Invalid email or password.");

        return Page();
    }

    /// 
    /// Google Sign-In / Sign-Up. The browser sends the ID token Google gave it; nothing is
    /// trusted until IGoogleAuthService has verified it on the server. Only ever signs in
    /// (or creates) an account whose Google identity is already linked or brand new - it
    /// never silently attaches Google to an existing password account, because anyone could
    /// have pre-registered that email address (account pre-hijacking).
    /// 
    public async Task<IActionResult> OnPostGoogleAsync(string? credential, string? returnUrl)
    {
        var (verified, message, identity) = await _googleAuth.VerifyAsync(credential ?? string.Empty, HttpContext.Session);
        if (!verified || identity == null)
            return Fail(message);

        var user = await _userManager.FindByLoginAsync(GoogleProvider, identity.Subject);
        if (user == null)
        {
            if (await _userManager.FindByEmailAsync(identity.Email) != null)
                return Fail("An account with this email already exists. Log in with your password, then connect Google from My Profile.");

            user = new ApplicationUser
            {
                UserName = identity.Email,
                Email = identity.Email,
                EmailConfirmed = true, // Google has verified it
                FullName = string.IsNullOrWhiteSpace(identity.Name) ? identity.Email : identity.Name,
                DateJoined = DateTime.UtcNow,
                IsActive = true
            };

            var created = await _userManager.CreateAsync(user);
            if (!created.Succeeded)
                return Fail("We couldn't create your account: " + string.Join(" ", created.Errors.Select(e => e.Description)));

            await _userManager.AddToRoleAsync(user, Roles.Customer);
            await _userManager.AddLoginAsync(user, new UserLoginInfo(GoogleProvider, identity.Subject, GoogleProvider));
        }

        if (!user.IsActive)
            return Fail("This account has been deactivated. Please contact the restaurant.");

        await _signInManager.SignInAsync(user, isPersistent: false);
        return new JsonResult(new { success = true, redirectUrl = await LandingUrlAsync(user, returnUrl) });
    }

    private static JsonResult Fail(string message) =>
        new(new { success = false, message }) { StatusCode = StatusCodes.Status400BadRequest };

    /// 
    /// Where to send someone after signing in. If they were heading somewhere specific (and it is a local address)
    /// they go there; otherwise each role lands in its own area - administrators on their dashboard, staff on theirs,
    /// customers on the home page.
    /// 
    private async Task<string> LandingUrlAsync(ApplicationUser user, string? returnUrl)
    {
        var requested = SafeReturnUrl(returnUrl);
        var home = Url.Content("~/");
        if (requested != home) return requested;

        if (await _userManager.IsInRoleAsync(user, Roles.Administrator)) return Url.Page("/Admin/Dashboard") ?? home;
        if (await _userManager.IsInRoleAsync(user, Roles.Staff)) return Url.Page("/Staff/Dashboard") ?? home;
        return home;
    }

    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

    public class InputModel
    {
        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
