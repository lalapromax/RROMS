using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using RROMS.Services;

namespace RROMS.Pages.Shared;

/// 
/// What a guest, a staff member or an administrator sees when something is missing or has failed. One page, three cases:
/// not found (404), not for this account (401/403) and "our side" (everything else, including unhandled exceptions).
/// The wording never blames the visitor. The "our side" case also prints a short reference that is written to the server log
/// next to the full request id, so a phone call can be matched to what really happened.
/// 
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    private readonly ILogger<ErrorModel> _logger;
    private readonly IMemoryCache _cache;

    public ErrorModel(ILogger<ErrorModel> logger, IMemoryCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    /// missing, denied or ours.
    public string Kind { get; private set; } = "ours";
    public int Status { get; private set; } = 500;

    /// Eight characters from the request id, as 7F3A-21C9. Only shown for the "our side" case.
    public string Reference { get; private set; } = string.Empty;

    /// The restaurant phone as last seen by any page. Empty until the owner has set one (the seed holds a placeholder in brackets).
    public string? Phone { get; private set; }

    /// Where "Try again" goes: the address that failed. A plain visit to /Error goes home.
    public string RetryUrl { get; private set; } = "/";

    public bool Signed => User.Identity?.IsAuthenticated == true;

    public void OnGet(int? code)
    {
        var thrown = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var reExecuted = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();

        // An exception always means "our side", whatever the query string says. Otherwise the status code picks the card.
        Status = thrown != null ? 500 : code is >= 400 and <= 599 ? code.Value : 500;
        Kind = Status == 404 ? "missing" : Status is 401 or 403 ? "denied" : "ours";

        if (thrown != null)
            RetryUrl = UriHelper.BuildRelative(Request.PathBase, thrown.Path, Request.QueryString);
        else if (reExecuted != null)
            RetryUrl = UriHelper.BuildRelative(reExecuted.OriginalPathBase, reExecuted.OriginalPath, new QueryString(reExecuted.OriginalQueryString));

        if (Kind == "ours")
        {
            var id = HttpContext.TraceIdentifier;
            Reference = ReferenceFor(id);
            var phone = _cache.TryGetValue(ChromeService.PhoneCacheKey, out string? p) ? p : null;
            Phone = Models.RestaurantSetting.Shown(phone);

            var path = thrown?.Path ?? reExecuted?.OriginalPath;
            _logger.LogWarning("Error page shown. Reference {Reference} is request {RequestId}, path {Path}, status {Status}", Reference, id, path, Status);
        }
        Response.StatusCode = Status;
    }

    /// The same request id always gives the same reference, so it can be worked out again from the log.
    public static string ReferenceFor(string requestId)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestId)).AsSpan(0, 4));
        return hex[..4] + "-" + hex[4..];
    }
}
