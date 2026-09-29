using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;

namespace RROMS.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LogoutModel(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        await _signInManager.SignOutAsync();
        if (returnUrl != null)
            return LocalRedirect(returnUrl);

        return RedirectToPage("/Index");
    }

    public IActionResult OnGet()
    {
        return RedirectToPage("/Index");
    }
}
