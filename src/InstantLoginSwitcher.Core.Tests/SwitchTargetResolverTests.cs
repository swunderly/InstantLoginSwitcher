using InstantLoginSwitcher.Core.Models;
using InstantLoginSwitcher.Core.Services;

namespace InstantLoginSwitcher.Core.Tests;

public sealed class SwitchTargetResolverTests
{
    private readonly SwitchTargetResolver _resolver = new();

    [Fact]
    public void ResolveTargets_MatchesCanonicalHotkeyAgainstLegacyStoredOrder()
    {
        var config = new SwitcherConfig
        {
            Profiles =
            [
                new SwitchProfile
                {
                    Name = "Alice-Bob",
                    UserA = "alice",
                    UserB = "bob",
                    Hotkey = "Alt+Ctrl+S",
                    Enabled = true
                }
            ],
            Users =
            [
                new StoredUserCredential
                {
                    UserName = "bob",
                    FullName = "Bob",
                    Qualified = "PC\\bob",
                    PasswordEncrypted = "encrypted"
                }
            ]
        };

        var result = _resolver.ResolveTargets(config, "Ctrl+Alt+S", "alice");

        Assert.Single(result);
        Assert.Equal("bob", result[0].UserName);
    }

    [Fact]
    public void ResolveTargets_IgnoresProfilesWithInvalidHotkeys()
    {
        var config = new SwitcherConfig
        {
            Profiles =
            [
                new SwitchProfile
                {
                    Name = "Broken",
                    UserA = "alice",
                    UserB = "bob",
                    Hotkey = "not-a-hotkey",
                    Enabled = true
                }
            ],
            Users =
            [
                new StoredUserCredential
                {
                    UserName = "bob",
                    FullName = "Bob",
                    Qualified = "PC\\bob",
                    PasswordEncrypted = "encrypted"
                }
            ]
        };

        var result = _resolver.ResolveTargets(config, "Ctrl+Alt+S", "alice");

        Assert.Empty(result);
    }
}
