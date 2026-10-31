using Microsoft.AspNetCore.Identity;
using RROMS.Models;

namespace RROMS.Services;

///
/// Hashes passwords with BCrypt (adaptive, salted, deliberately slow) in place of Identity's built-in PBKDF2 hasher.
/// Every password set from now on - registration, change or reset, and the temporary passwords administrators give
/// staff - is stored as a BCrypt hash.
///
/// Accounts created before the switch still hold the old Identity hash. They keep working: the old hash is checked
/// once more, and a correct password answers <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>, which makes
/// Identity re-save it as BCrypt straight away. So every account moves over at its next sign-in, and nobody has to
/// reset a password.
///
/// The password is pre-hashed with SHA-384 (BCrypt.Net's "enhanced" mode) because plain BCrypt silently ignores
/// anything after the 72nd byte, and passwords here may be up to 100 characters.
///
public class BCryptPasswordHasher : IPasswordHasher<ApplicationUser>
{
    /// 2^12 rounds: roughly a quarter of a second per check on a typical server.
    public const int WorkFactor = 12;

    private const BCrypt.Net.HashType PreHash = BCrypt.Net.HashType.SHA384;

    private readonly PasswordHasher<ApplicationUser> _legacy = new();

    public string HashPassword(ApplicationUser user, string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password, PreHash, WorkFactor);

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || providedPassword == null)
            return PasswordVerificationResult.Failed;

        if (!IsBCrypt(hashedPassword))
        {
            // A hash from before the switch: if it matches, ask Identity to store the password again as BCrypt.
            return _legacy.VerifyHashedPassword(user, hashedPassword, providedPassword) == PasswordVerificationResult.Failed
                ? PasswordVerificationResult.Failed
                : PasswordVerificationResult.SuccessRehashNeeded;
        }

        try
        {
            if (!BCrypt.Net.BCrypt.EnhancedVerify(providedPassword, hashedPassword, PreHash))
                return PasswordVerificationResult.Failed;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return PasswordVerificationResult.Failed;
        }

        // Raising the work factor later upgrades older BCrypt hashes the same way.
        return BCrypt.Net.BCrypt.PasswordNeedsRehash(hashedPassword, WorkFactor)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static bool IsBCrypt(string hash) => hash.StartsWith("$2", StringComparison.Ordinal);
}
