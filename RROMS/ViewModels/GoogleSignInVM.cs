namespace RROMS.ViewModels;

/// Inputs for the Google button partial. ButtonText is a Google Identity Services
/// text key: "signin_with", "signup_with" or "continue_with". PostUrl is the page handler that
/// verifies the token on the server (login/sign-up, or link-to-current-account).
public record GoogleSignInVM(string ClientId, string PostUrl, string? ReturnUrl, string ButtonText = "continue_with");
