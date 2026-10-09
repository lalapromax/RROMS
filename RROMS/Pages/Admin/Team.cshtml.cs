using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// <summary>
/// The team. An administrator can do exactly two things to a staff account: add one, or terminate one. Roles are fixed
/// (Staff and Administrator, nothing in between and nothing new), the restaurant name is fixed, and there is no handler on this
/// page, or anywhere else, that changes a person's role or makes another administrator. A terminated account is kept, never deleted,
/// so everything that person did stays attributed to them. Adding the same email again brings the account back.
/// </summary>
public class TeamModel : PageModel
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;
    private readonly IEmailService _email;

    public TeamModel(UserManager<ApplicationUser> users, IAuditService audit, IEmailService email)
    {
        _users = users;
        _audit = audit;
        _email = email;
    }

    public record Person(string Id, string Name, string Email, string Role, bool Active, bool IsMe, string? Diet, string? Allergies);

    public List<Person> People { get; private set; } = new();
    public List<AdminAuditLog> Recent { get; private set; } = new();
    public string MeId { get; private set; } = "";
    public bool EmailReady => _email.IsConfigured;
    public bool OpenAdd { get; private set; }

    [BindProperty] public AddInput Add { get; set; } = new();

    public class AddInput
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        MeId = _users.GetUserId(User) ?? "";
        var staff = await _users.GetUsersInRoleAsync(Roles.Staff);
        var admins = await _users.GetUsersInRoleAsync(Roles.Administrator);
        People = staff.OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName)
            .Select(u => new Person(u.Id, u.FullName, u.Email ?? "", Roles.Staff, u.IsActive, false, u.DietaryPreference, u.Allergies))
            .Concat(admins.OrderBy(u => u.FullName)
                .Select(u => new Person(u.Id, u.FullName, u.Email ?? "", Roles.Administrator, u.IsActive, u.Id == MeId, null, null)))
            .ToList();
        Recent = await _audit.GetRecentAsync(6);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Add a staff member. They choose their own password from an emailed link; nobody else ever knows it.
    // ---------------------------------------------------------------------------------------------------------------
    public async Task<IActionResult> OnPostAddAsync()
    {
        var name = (Add.FullName ?? "").Trim();
        var email = (Add.Email ?? "").Trim();

        string? problem = null;
        if (name.Length == 0 || name.Length > 100) problem = "Please enter the person's full name (up to 100 characters).";
        else if (email.Length > 120 || !new EmailAddressAttribute().IsValid(email)) problem = "Please enter a valid email address.";

        ApplicationUser? user = problem == null ? await _users.FindByEmailAsync(email) : null;
        var returning = false;
        if (problem == null && user != null)
        {
            if (user.IsActive) problem = "An account with that email already exists.";
            else if (!await _users.IsInRoleAsync(user, Roles.Staff)) problem = "That email belongs to an account that cannot be added as staff.";
            else returning = true;   // a terminated staff account: bring it back rather than make a second one
        }
        if (problem != null)
        {
            ModelState.AddModelError("Add", problem);
            OpenAdd = true;
            await LoadAsync();
            return Page();
        }

        if (returning)
        {
            user!.IsActive = true;
            user.TerminatedAt = null;
            user.FullName = name;
            var back = await _users.UpdateAsync(user);
            if (!back.Succeeded) return await Fail(string.Join(" ", back.Errors.Select(e => e.Description)));
            await _users.UpdateSecurityStampAsync(user);
        }
        else
        {
            user = new ApplicationUser { UserName = email, Email = email, FullName = name, EmailConfirmed = true, IsActive = true, DateJoined = DateTime.UtcNow };
            // No password yet: the person sets one from the invitation link.
            var created = await _users.CreateAsync(user);
            if (!created.Succeeded) return await Fail(string.Join(" ", created.Errors.Select(e => e.Description)));
            await _users.AddToRoleAsync(user, Roles.Staff);
        }

        var acting = await _users.GetUserAsync(User);
        if (acting != null)
            await _audit.LogAsync(acting.Id, acting.FullName, returning ? "ReactivateStaff" : "AddStaff", $"{name} ({email})");

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var link = Url.Page("/Account/ResetPassword", null, new { email = user.Email, code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)) }, Request.Scheme)!;

        var sent = false;
        if (_email.IsConfigured)
        {
            var (ok, _) = await _email.SendAsync(user.Email!, $"Your {Brand.Name} staff account",
                $"Hello {name},\n\nYou have been added to the {Brand.Name} team. Choose your password here to sign in:\n\n{link}\n\nThe link works once and expires after a day. If it has expired, use \"Forgot your password?\" on the sign-in page.\n\n{Brand.Name}");
            sent = ok;
        }

        if (sent)
            TempData["StatusMessage"] = $"{name} was added as Staff. An invitation is on its way to {email}.";
        else
        {
            // Mail is not set up (or failed): show the link once so the administrator can hand it over in person.
            TempData["StatusMessage"] = $"{name} was added as Staff.";
            TempData["InviteName"] = name;
            TempData["InviteLink"] = link;
        }
        return RedirectToPage();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Terminate. Asks for the administrator's own password every time; checked on the server.
    // ---------------------------------------------------------------------------------------------------------------
    public async Task<IActionResult> OnPostTerminateAsync(string id, string confirmPassword)
    {
        var acting = await _users.GetUserAsync(User);
        if (acting == null) return Forbid();

        if (string.IsNullOrEmpty(confirmPassword) || !await _users.CheckPasswordAsync(acting, confirmPassword))
            return await Fail("Your password was not correct, so the account was not terminated.");

        var user = await _users.FindByIdAsync(id ?? "");
        if (user == null) return await Fail("That account no longer exists.");
        // Only staff accounts can be terminated here: never an administrator, never a guest, never yourself.
        if (user.Id == acting.Id || !await _users.IsInRoleAsync(user, Roles.Staff) || await _users.IsInRoleAsync(user, Roles.Administrator))
            return await Fail("Only staff accounts can be terminated.");
        if (!user.IsActive) return await Fail($"{user.FullName} has already been terminated.");

        user.IsActive = false;
        user.TerminatedAt = DateTime.UtcNow;
        var done = await _users.UpdateAsync(user);
        if (!done.Succeeded) return await Fail(string.Join(" ", done.Errors.Select(e => e.Description)));
        // Changing the security stamp ends any session they have open at the next check (within a minute).
        await _users.UpdateSecurityStampAsync(user);

        await _audit.LogAsync(acting.Id, acting.FullName, "TerminateStaff", $"{user.FullName} ({user.Email})");
        TempData["StatusMessage"] = $"{user.FullName}'s account was terminated. They will be signed out within a minute.";
        return RedirectToPage();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Staff dietary information, for planning staff meals. Staff accounts only.
    // ---------------------------------------------------------------------------------------------------------------
    public async Task<IActionResult> OnPostDietaryAsync(string id, string? preference, string? other, string? allergies)
    {
        var user = await _users.FindByIdAsync(id ?? "");
        if (user == null || !await _users.IsInRoleAsync(user, Roles.Staff))
            return await Fail("Dietary information can only be recorded for staff accounts.");

        var pref = preference == DietaryOptions.Other && !string.IsNullOrWhiteSpace(other) ? other.Trim() : (preference ?? "").Trim();
        allergies = string.IsNullOrWhiteSpace(allergies) ? null : allergies.Trim();
        if (pref.Length > 100 || (allergies?.Length ?? 0) > 300) return await Fail("That text is too long.");

        user.DietaryPreference = string.IsNullOrEmpty(pref) || pref == DietaryOptions.Other ? null : pref;
        user.Allergies = allergies;
        await _users.UpdateAsync(user);

        var acting = await _users.GetUserAsync(User);
        if (acting != null) await _audit.LogAsync(acting.Id, acting.FullName, "UpdateStaffDietaryInfo", $"{user.FullName} ({user.Email})");
        TempData["StatusMessage"] = $"Dietary information saved for {user.FullName}.";
        return RedirectToPage();
    }

    private Task<IActionResult> Fail(string message)
    {
        TempData["ErrorMessage"] = message;
        return Task.FromResult<IActionResult>(RedirectToPage());
    }
}
