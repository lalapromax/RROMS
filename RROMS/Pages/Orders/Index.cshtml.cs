using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Presentation;
using RROMS.Services;

namespace RROMS.Pages.Orders;

public class IndexModel : PageModel
{
    /// <summary>How many orders show before "Show all".</summary>
    public const int FirstPage = 6;

    private readonly IOrderService _orderService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(IOrderService orderService, UserManager<ApplicationUser> userManager)
    {
        _orderService = orderService;
        _userManager = userManager;
    }

    /// <summary>all, paid, unpaid or cancelled.</summary>
    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool All { get; set; }

    public int EverOrdered { get; private set; }
    public int Matching { get; private set; }
    public List<Order> Orders { get; private set; } = new();
    public bool Narrowed => Filter != "all" || !string.IsNullOrWhiteSpace(Q);

    public async Task OnGetAsync()
    {
        Filter = Filter?.ToLowerInvariant() is "paid" or "unpaid" or "cancelled" ? Filter!.ToLowerInvariant() : "all";
        var userId = _userManager.GetUserId(User)!;
        var mine = await _orderService.GetCustomerOrdersAsync(userId);
        EverOrdered = mine.Count;

        IEnumerable<Order> rows = mine;
        if (Filter != "all") rows = rows.Where(o => OrderView.Pay(o) == Filter);

        var digits = new string((Q ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length > 0) rows = rows.Where(o => o.Id.ToString().Contains(digits));

        var list = rows.ToList();
        Matching = list.Count;
        Orders = All ? list : list.Take(FirstPage).ToList();
    }
}
