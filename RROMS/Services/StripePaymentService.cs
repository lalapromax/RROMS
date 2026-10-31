using Stripe;

namespace RROMS.Services;

/// 
/// Thin wrapper around Stripe.net for the one thing this app needs: creating a
/// PaymentIntent for "Online payment" checkouts and verifying the Payment Element
/// result (and refunding it if the order is cancelled). Payment status is always
/// confirmed by asking Stripe's API at checkout; no webhooks are used. Cash/Card
/// stay staff-settled-later (brief 6.C) and never touch this service.
///
/// Credentials come from configuration (StripeSettings:* in appsettings.json, which
/// should be overridden with real TEST keys via environment variables or user-secrets
/// - never hardcoded). Until those keys are supplied, <see cref="IsConfigured"/> is
/// false and the Cart page disables "Online payment" instead of pretending it works.
/// 
public interface IStripePaymentService
{
    /// True only when a publishable key, secret key, and the feature flag are all set.
    bool IsConfigured { get; }
    string PublishableKey { get; }
    string Currency { get; }

    /// 
    /// Creates a PaymentIntent for the given amount (already computed server-side from
    /// real database prices - never from anything the browser sent) and returns the
    /// client secret the frontend needs to mount Stripe's embedded Payment Element.
    /// Created before the Order row exists (the customer hasn't confirmed payment yet),
    /// so the reference is the customer's id, not an order id.
    /// 
    /// <param name="onlineBanking">True for the "Online banking" choice: the guest pays through their own bank (Stripe FPX, which needs
    /// the account currency to be MYR). That method always passes through the bank's page and back, so it cannot stay embedded.</param>
    Task<(string ClientSecret, string PaymentIntentId)> CreatePaymentIntentAsync(decimal amount, string customerId, bool onlineBanking = false);

    /// Re-fetches a PaymentIntent from Stripe's API (never trusts a client-reported status).
    Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId);

    /// Sends the whole payment back to the guest. Already-refunded payments count as done, so a repeat is safe.
    Task<(bool Success, string Message)> RefundAsync(string paymentIntentId);
}

public class StripePaymentService : IStripePaymentService
{
    private readonly IConfiguration _config;
    private readonly PaymentIntentService _paymentIntentService = new();

    public StripePaymentService(IConfiguration config)
    {
        _config = config;
    }

    private string SecretKey => _config["StripeSettings:SecretKey"] ?? string.Empty;

    /// On as soon as real keys are pasted in (placeholders such as pk_test_YOUR_PUBLISHABLE_KEY do not
    /// count). "StripeSettings:Enabled": false is an optional off-switch; leaving it out means on.
    public bool IsConfigured =>
        (_config.GetValue<bool?>("StripeSettings:Enabled") ?? true)
        && IntegrationSettings.IsReal(PublishableKey)
        && IntegrationSettings.IsReal(SecretKey);

    public string PublishableKey => _config["StripeSettings:PublishableKey"] ?? string.Empty;
    public string Currency => string.IsNullOrWhiteSpace(_config["StripeSettings:Currency"]) ? "myr" : _config["StripeSettings:Currency"]!.ToLowerInvariant();

    public async Task<(string ClientSecret, string PaymentIntentId)> CreatePaymentIntentAsync(decimal amount, string customerId, bool onlineBanking = false)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Stripe is not configured. Set StripeSettings:Enabled, PublishableKey and SecretKey first.");

        // Stripe amounts are integer minor units (sen for myr), never decimal major units.
        var minorUnits = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

        var options = new PaymentIntentCreateOptions
        {
            Amount = minorUnits,
            Currency = Currency,
            Description = onlineBanking ? "Online banking payment for an order" : "Online order payment",
            Metadata = new Dictionary<string, string>
            {
                { "rroms_customer_id", customerId }
            }
        };

        if (onlineBanking)
        {
            // Online banking (FPX) is a redirect method: the guest approves the payment on their own bank's page and returns.
            options.PaymentMethodTypes = new List<string> { "fpx" };
        }
        else
        {
            // AllowRedirects = "never" keeps card payments embedded end to end (brief: "use embedded UI instead of bringing
            // the customer to a new site"): Stripe only offers methods that can confirm in the page.
            options.AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true, AllowRedirects = "never" };
        }

        var requestOptions = new RequestOptions { ApiKey = SecretKey };
        var intent = await _paymentIntentService.CreateAsync(options, requestOptions);
        return (intent.ClientSecret, intent.Id);
    }

    public async Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId)
    {
        var requestOptions = new RequestOptions { ApiKey = SecretKey };
        return await _paymentIntentService.GetAsync(paymentIntentId, requestOptions: requestOptions);
    }

    public async Task<(bool Success, string Message)> RefundAsync(string paymentIntentId)
    {
        if (!IsConfigured)
            return (false, "Online payments are not switched on, so the payment could not be returned automatically.");
        try
        {
            await new RefundService().CreateAsync(new RefundCreateOptions { PaymentIntent = paymentIntentId },
                new RequestOptions { ApiKey = SecretKey });
            return (true, "Refunded.");
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "charge_already_refunded")
        {
            return (true, "Already refunded.");
        }
        catch (StripeException ex)
        {
            return (false, string.IsNullOrWhiteSpace(ex.StripeError?.Message) ? "Stripe would not return the payment." : ex.StripeError!.Message!);
        }
    }
}
