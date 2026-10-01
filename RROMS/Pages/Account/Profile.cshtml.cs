using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;

namespace RROMS.Pages.Account;

public class ProfileModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ProfileModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public ProfileInputModel ProfileInput { get; set; } = new();

    [BindProperty]
    public PasswordInputModel PasswordInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        Email = user.Email ?? string.Empty;
        ProfileInput = new ProfileInputModel
        {
            FullName = user.FullName,
            Phone = user.PhoneNumber,
            Address = user.Address,
            DietaryPreference = user.DietaryPreference
        };
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        ModelState.Clear();
        if (!TryValidateModel(ProfileInput, nameof(ProfileInput)))
        {
            Email = user.Email ?? string.Empty;
            return Page();
        }

        user.FullName = ProfileInput.FullName;
        user.PhoneNumber = ProfileInput.Phone;
        user.Address = ProfileInput.Address;
        user.DietaryPreference = ProfileInput.DietaryPreference;

        await _userManager.UpdateAsync(user);
        TempData["StatusMessage"] = "Your profile has been updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        ModelState.Clear();
        if (!TryValidateModel(PasswordInput, nameof(PasswordInput)))
        {
            Email = user.Email ?? string.Empty;
            ProfileInput = new ProfileInputModel { FullName = user.FullName, Phone = user.PhoneNumber, Address = user.Address, DietaryPreference = user.DietaryPreference };
            return Page();
        }

        var result = await _userManager.ChangePasswordAsync(user, PasswordInput.CurrentPassword, PasswordInput.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            Email = user.Email ?? string.Empty;
            ProfileInput = new ProfileInputModel { FullName = user.FullName, Phone = user.PhoneNumber, Address = user.Address, DietaryPreference = user.DietaryPreference };
            return Page();
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["StatusMessage"] = "Your password has been changed.";
        return RedirectToPage();
    }

    public class ProfileInputModel
    {
        [Required, StringLength(100), Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Phone, Display(Name = "Phone number")]
        public string? Phone { get; set; }

        [StringLength(300), Display(Name = "Address")]
        public string? Address { get; set; }

        [StringLength(200), Display(Name = "Dietary preference")]
        public string? DietaryPreference { get; set; }
    }

    public class PasswordInputModel
    {
        [Required, DataType(DataType.Password), Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
