using System.Security.Cryptography;
using System.Text;

namespace InstantLoginSwitcher.Core.Services;

public sealed class PasswordProtector
{
    private const string DpapiPrefix = "DPAPI:";
    private const string LegacyBase64Prefix = "B64:";

    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            throw new InvalidOperationException("Password cannot be blank.");
        }

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        return DpapiPrefix + Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string encryptedText)
    {
        if (string.IsNullOrWhiteSpace(encryptedText))
        {
            return string.Empty;
        }

        if (encryptedText.StartsWith(LegacyBase64Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var legacyPayload = encryptedText[LegacyBase64Prefix.Length..];
            var plainBytes = Convert.FromBase64String(legacyPayload);
            return Encoding.UTF8.GetString(plainBytes);
        }

        var payload = encryptedText.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase)
            ? encryptedText[DpapiPrefix.Length..]
            : encryptedText;

        var protectedBytes = Convert.FromBase64String(payload);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
