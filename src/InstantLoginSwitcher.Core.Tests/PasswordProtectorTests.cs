using InstantLoginSwitcher.Core.Services;
using System.Text;

namespace InstantLoginSwitcher.Core.Tests;

public sealed class PasswordProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsPassword()
    {
        var protector = new PasswordProtector();
        var source = "MyStrongPass!123";

        var encrypted = protector.Protect(source);
        var decrypted = protector.Unprotect(encrypted);

        Assert.NotEqual(source, encrypted);
        Assert.Equal(source, decrypted);
    }

    [Fact]
    public void Unprotect_SupportsLegacyBase64Prefix()
    {
        var protector = new PasswordProtector();
        var source = "LegacyPass!789";
        var encoded = "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(source));

        var decrypted = protector.Unprotect(encoded);

        Assert.Equal(source, decrypted);
    }
}
