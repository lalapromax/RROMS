using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;

namespace RROMS.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailService _email;

    public ForgotPasswordModel(UserManager<ApplicationUser> users, IEmailService email)
    {
        _users = users;
        _email = email;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool EmailConfigured => _email.IsConfigured;
    public bool Sent { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // The same answer whether or not the address has an account, so this page cannot be used to find out who has one.
        Sent = true;
        var user = await _users.FindByEmailAsync(Input.Email.Trim());
        if (user == null || !user.IsActive || !_email.IsConfigured) return Page();

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Page("/Account/ResetPassword", null, new { email = user.Email, code = encoded }, Request.Scheme)!;
        await _email.SendAsync(user.Email!, $"Reset your {Brand.Name} password",
            $"Hello {user.FullName},\n\nSomeone asked to reset the password for this account. If it was you, choose a new one here:\n\n{link}\n\nThe link works once and expires soon. If you did not ask, you can ignore this email.\n\n{Brand.Name}");
        return Page();
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = "That does not look like an email address."), Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}
