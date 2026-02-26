using InstantLoginSwitcher.Core.Services;

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
}
