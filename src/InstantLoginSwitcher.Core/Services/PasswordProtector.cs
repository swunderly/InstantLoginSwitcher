using System.Security.Cryptography;
using System.Text;

namespace InstantLoginSwitcher.Core.Services;

public sealed class PasswordProtector
{
    private const string Prefix = "DPAPI:";

    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            throw new InvalidOperationException("Password cannot be blank.");
        }

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string encryptedText)
    {
        if (string.IsNullOrWhiteSpace(encryptedText))
        {
            return string.Empty;
        }

        var payload = encryptedText.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? encryptedText[Prefix.Length..]
            : encryptedText;

        var protectedBytes = Convert.FromBase64String(payload);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
