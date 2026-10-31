using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Admin;

/// <summary>
/// One page that answers "is Google / Stripe / email / the chat model actually set up?" and lets the administrator
/// send a real test email or test question. It only ever shows whether a value is set (and the last four characters),
/// never the secret itself. Where to get each value: INTEGRATIONS.md.
/// </summary>
public class IntegrationsModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IGoogleAuthService _google;
    private readonly IStripePaymentService _stripe;
    private readonly IEmailService _email;
    private readonly ILlmChatClient _llm;
    private readonly UserManager<ApplicationUser> _userManager;

    public IntegrationsModel(IConfiguration config, IGoogleAuthService google, IStripePaymentService stripe, IEmailService email, ILlmChatClient llm, UserManager<ApplicationUser> userManager)
    {
        _config = config;
        _google = google;
        _stripe = stripe;
        _email = email;
        _llm = llm;
        _userManager = userManager;
    }

    public string Origin => $"{Request.Scheme}://{Request.Host}";

    public bool GoogleOn => _google.IsConfigured;
    public string GoogleClientIdState => IntegrationSettings.Describe(_google.ClientId);

    public bool StripeOn => _stripe.IsConfigured;
    public string StripeSecretState => IntegrationSettings.Describe(_config["StripeSettings:SecretKey"]);
    public string StripePublishableState => IntegrationSettings.Describe(_config["StripeSettings:PublishableKey"]);
    public string StripeMode
    {
        get
        {
            var key = _config["StripeSettings:SecretKey"] ?? string.Empty;
            if (!IntegrationSettings.IsReal(key)) return "not set";
            return key.StartsWith("sk_live_", StringComparison.Ordinal) || key.StartsWith("rk_live_", StringComparison.Ordinal) ? "LIVE money" : "test mode (no real money)";
        }
    }
    public string StripeCurrency => _stripe.Currency;

    public bool EmailOn => _email.IsConfigured;
    public string SmtpHost => IntegrationSettings.IsReal(_config["SmtpSettings:Host"]) ? _config["SmtpSettings:Host"]! : "not set";
    public string SmtpPort => (_config.GetValue<int?>("SmtpSettings:Port") ?? 587).ToString();
    public string SmtpUser => IntegrationSettings.Describe(_config["SmtpSettings:Username"]);
    public string SmtpPassword => IntegrationSettings.Describe(_config["SmtpSettings:Password"]);

    public bool ChatOn => _llm.IsConfigured;
    public string ChatProvider => _llm.ProviderLabel;
    public string ChatModel => IntegrationSettings.IsReal(_config["ChatbotSettings:Model"]) ? _config["ChatbotSettings:Model"]! : "not set";
    public string ChatKey => IntegrationSettings.Describe(_config["ChatbotSettings:ApiKey"]);

    public string? AdminEmail { get; private set; }

    [TempData] public string? EmailTestResult { get; set; }
    [TempData] public string? EmailTestOk { get; set; }
    [TempData] public string? ChatTestResult { get; set; }
    [TempData] public string? ChatTestOk { get; set; }

    public async Task OnGetAsync()
    {
        var me = await _userManager.GetUserAsync(User);
        AdminEmail = me?.Email;
    }

    public async Task<IActionResult> OnPostTestEmailAsync()
    {
        var me = await _userManager.GetUserAsync(User);
        if (me?.Email == null)
        {
            EmailTestOk = "0";
            EmailTestResult = "Your account has no email address to send the test to.";
            return RedirectToPage();
        }

        // The test only ever goes to the signed-in administrator's own address - this button can't be used to send mail to strangers.
        var (ok, message) = await _email.SendAsync(me.Email, "RROMS test email",
            "If you can read this, your SMTP settings work. Customers will now receive reservation and order emails.");
        EmailTestOk = ok ? "1" : "0";
        EmailTestResult = ok ? $"Test email sent to {me.Email}. Check your inbox (and spam folder)." : message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestChatAsync()
    {
        if (!_llm.IsConfigured)
        {
            ChatTestOk = "0";
            ChatTestResult = "The chat model isn't set up (Provider, Model and ApiKey). The chat widget is using the built-in answers.";
            return RedirectToPage();
        }

        var reply = await _llm.AskAsync("You are a connection test. Reply with the single word OK.", "Say OK.", HttpContext.RequestAborted);
        ChatTestOk = reply == null ? "0" : "1";
        ChatTestResult = reply == null
            ? "The model did not answer. Check the Model name and ApiKey; the exact error is in the server console (look for 'Chat model returned')."
            : $"The model answered: \"{(reply.Length > 80 ? reply[..80] + "..." : reply)}\"";
        return RedirectToPage();
    }
}
