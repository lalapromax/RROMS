using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;

namespace RROMS.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public RegisterModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid)
            return Page();

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            PhoneNumber = Input.Phone,
            FullName = Input.FullName,
            DietaryPreference = Input.DietaryPreference,
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

    public class InputModel
    {
        [Required, StringLength(100), Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Phone, Display(Name = "Phone number (optional)")]
        public string? Phone { get; set; }

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [StringLength(200), Display(Name = "Dietary preference (optional)")]
        public string? DietaryPreference { get; set; }
    }
}
