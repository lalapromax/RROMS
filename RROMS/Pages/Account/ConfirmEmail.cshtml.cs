using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using RROMS.Models;

namespace RROMS.Pages.Account;

/// <summary>
/// The link in the email sent when someone changes their sign-in address on the profile page. The address only changes
/// here, once the new mailbox is proven to be theirs. The token is single use and tied to the new address.
/// </summary>
[AllowAnonymous]
public class ConfirmEmailModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;

    public ConfirmEmailModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn)
    {
        _users = users;
        _signIn = signIn;
    }

    public bool Done { get; private set; }
    public string? NewEmail { get; private set; }
    public string Message { get; private set; } = "That link is not valid. Ask for a new one from your profile.";

    public async Task<IActionResult> OnGetAsync(string? userId, string? email, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            return Page();

        var user = await _users.FindByIdAsync(userId);
        if (user == null || !user.IsActive) return Page();

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return Page(); }

        var taken = await _users.FindByEmailAsync(email);
        if (taken != null && taken.Id != user.Id)
        {
            Message = "That email now belongs to another account, so it can't be used here.";
            return Page();
        }

        var change = await _users.ChangeEmailAsync(user, email, token);
        if (!change.Succeeded)
        {
            Message = "That link has already been used or has expired. Ask for a new one from your profile.";
            return Page();
        }

        // Sign-in is by email, so the user name follows it.
        await _users.SetUserNameAsync(user, email);

        // The security stamp changed, which ends every open session. Sign out here so the next step is a clean sign-in.
        await _signIn.SignOutAsync();
        Done = true;
        NewEmail = email;
        return Page();
    }
}
