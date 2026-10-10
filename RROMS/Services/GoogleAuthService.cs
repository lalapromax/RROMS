using Google.Apis.Auth;

namespace RROMS.Services;

public record GoogleIdentity(string Subject, string Email, string? Name);

public interface IGoogleAuthService
{
    bool IsConfigured { get; }
    string ClientId { get; }

    /// Creates a one-time nonce, remembers it in the session, and returns it for the page to hand to Google.
    string CreateNonce(ISession session);

    /// 
    /// Verifies a Google ID token ("credential") on the server: Google's signature, the issuer,
    /// the expiry, that it was issued for THIS app's client id, that Google has verified the
    /// email address, and that it carries the nonce this session asked for.
    /// A successful frontend callback alone proves nothing - only this check does (brief 6.D).
    /// 
    Task<(bool Success, string Message, GoogleIdentity? Identity)> VerifyAsync(string credential, ISession session);
}

public class GoogleAuthService : IGoogleAuthService
{
    private const string NonceKey = "GoogleSignInNonce";

    private readonly IConfiguration _config;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(IConfiguration config, ILogger<GoogleAuthService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string ClientId => _config["GoogleSettings:ClientId"] ?? string.Empty;

    /// On as soon as a real Client ID is pasted in (placeholders like YOUR_GOOGLE_CLIENT_ID do not count).
    /// "GoogleSettings:Enabled": false is an optional off-switch; leaving it out means on.
    public bool IsConfigured =>
        (_config.GetValue<bool?>("GoogleSettings:Enabled") ?? true) && IntegrationSettings.IsReal(ClientId);

    public string CreateNonce(ISession session)
    {
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        session.SetString(NonceKey, nonce);
        return nonce;
    }

    public async Task<(bool Success, string Message, GoogleIdentity? Identity)> VerifyAsync(string credential, ISession session)
    {
        if (!IsConfigured)
            return (false, "Google sign-in isn't set up on this server.", null);

        if (string.IsNullOrWhiteSpace(credential))
            return (false, "Google did not return a sign-in token. Please try again.", null);

        var expectedNonce = session.GetString(NonceKey);
        session.Remove(NonceKey); // single use, whatever the outcome
        if (string.IsNullOrEmpty(expectedNonce))
            return (false, "Your sign-in page expired. Please refresh the page and try again.", null);

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { ClientId }
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(credential, settings);

            if (!string.Equals(payload.Nonce, expectedNonce, StringComparison.Ordinal))
                return (false, "That sign-in could not be verified. Please refresh the page and try again.", null);

            if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
                return (false, "Your Google account's email address is not verified.", null);

            return (true, string.Empty, new GoogleIdentity(payload.Subject, payload.Email, payload.Name));
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Rejected an invalid Google ID token.");
            return (false, "That Google sign-in could not be verified. Please try again.", null);
        }
    }
}
