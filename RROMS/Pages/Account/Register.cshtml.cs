using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IGoogleAuthService _googleAuth;

    public RegisterModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IGoogleAuthService googleAuth)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _googleAuth = googleAuth;
    }

    public bool GoogleEnabled => _googleAuth.IsConfigured;
    public string GoogleClientId => _googleAuth.ClientId;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl = SafeReturnUrl(returnUrl);

        if (!ModelState.IsValid)
            return Page();

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            PhoneNumber = Input.Phone,
            FullName = Input.FullName,
            DietaryPreference = DietaryRules.JoinNeeds(Input.DietaryNeeds),
            Allergies = DietaryRules.JoinAllergies(Input.AllergyChips, Input.AllergyOther),
            NoticeAnnouncements = Input.NewsOptIn,
            DateJoined = DateTime.UtcNow,
            IsActive = true,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, Roles.Customer);
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return Page();
    }

    // Only ever redirect back to a path on this site - never to a link someone put in the URL.
    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

    public class InputModel
    {
        [Required(ErrorMessage = "Tell us your name."), StringLength(100), Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = "That does not look like an email address."), Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Phone, Display(Name = "Phone")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Choose a password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters."), DataType(DataType.Password), Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        /// The diet chips ticked (see DietaryRules.NeedChips).
        public List<string> DietaryNeeds { get; set; } = new();

        /// The allergy chips ticked (see DietaryRules.AllergyChips).
        public List<string> AllergyChips { get; set; } = new();

        [StringLength(150)]
        public string? AllergyOther { get; set; }

        [Range(typeof(bool), "true", "true", ErrorMessage = "Please agree to continue.")]
        public bool AgreeToTerms { get; set; }

        public bool NewsOptIn { get; set; }
    }
}
