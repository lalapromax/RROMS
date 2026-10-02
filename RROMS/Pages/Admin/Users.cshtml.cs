using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;

namespace RROMS.Pages.Admin;

public class UsersModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UsersModel(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public List<UserRow> Users { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var all = _userManager.Users.OrderBy(u => u.FullName).ToList();
        var rows = new List<UserRow>();
        foreach (var u in all)
        {
            var roles = await _userManager.GetRolesAsync(u);
            rows.Add(new UserRow
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                Phone = u.PhoneNumber,
                Role = roles.FirstOrDefault() ?? "(none)",
                IsActive = u.IsActive,
                DateJoined = u.DateJoined
            });
        }
        Users = rows;
    }

    public async Task<IActionResult> OnPostCreateStaffAsync(string fullName, string email, string password, string role)
    {
        if (role != Roles.Staff && role != Roles.Administrator)
            role = Roles.Staff;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            IsActive = true,
            DateJoined = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, role);
            TempData["StatusMessage"] = $"{role} account created for {fullName}.";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user != null)
        {
            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);
            TempData["StatusMessage"] = $"{user.FullName} is now {(user.IsActive ? "active" : "deactivated")}.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(string id, string newRole)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) { TempData["ErrorMessage"] = "User not found."; return RedirectToPage(); }
        if (!Roles.All.Contains(newRole)) { TempData["ErrorMessage"] = "Invalid role."; return RedirectToPage(); }

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, newRole);

        TempData["StatusMessage"] = $"{user.FullName} is now a {newRole}.";
        return RedirectToPage();
    }

    public class UserRow
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime DateJoined { get; set; }
    }
}
