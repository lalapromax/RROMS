using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using RROMS.Models;

namespace RROMS.Pages.Account;

[AllowAnonymous]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;

    public ResetPasswordModel(UserManager<ApplicationUser> users) => _users = users;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IActionResult OnGet(string? email, string? code)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            return RedirectToPage("/Account/ForgotPassword");
        Input = new InputModel { Email = email, Code = code };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _users.FindByEmailAsync(Input.Email);
        // Same wording whether the account exists or not.
        if (user == null || !user.IsActive)
        {
            TempData["StatusMessage"] = "Your password has been changed. You can sign in now.";
            return RedirectToPage("/Account/Login");
        }

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code)); }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "That link is not valid. Ask for a new one.");
            return Page();
        }

        var result = await _users.ResetPasswordAsync(user, token, Input.Password);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "Your password has been changed. You can sign in now.";
            return RedirectToPage("/Account/Login");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Code == "InvalidToken" ? "That link has expired or was already used. Ask for a new one." : error.Description);
        return Page();
    }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose a new password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters."), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password), Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
