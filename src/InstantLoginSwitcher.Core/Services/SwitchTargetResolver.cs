using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.Core.Services;

public sealed class SwitchTargetResolver
{
    public IReadOnlyList<SwitchTarget> ResolveTargets(SwitcherConfig config, string hotkeyCanonical, string currentUser)
    {
        var map = new Dictionary<string, SwitchTarget>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in config.Profiles.Where(profile => profile.Enabled))
        {
            if (!profile.Hotkey.Equals(hotkeyCanonical, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? targetUser = null;
            if (profile.UserA.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
            {
                targetUser = profile.UserB;
            }
            else if (profile.UserB.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
            {
                targetUser = profile.UserA;
            }

            if (string.IsNullOrWhiteSpace(targetUser) || map.ContainsKey(targetUser))
            {
                continue;
            }

            var credential = config.Users.FirstOrDefault(user =>
                user.UserName.Equals(targetUser, StringComparison.OrdinalIgnoreCase));
            if (credential is null)
            {
                continue;
            }

            map[targetUser] = new SwitchTarget
            {
                UserName = credential.UserName,
                FullName = string.IsNullOrWhiteSpace(credential.FullName) ? credential.UserName : credential.FullName,
                Qualified = credential.Qualified,
                PicturePath = credential.PicturePath
            };
        }

        return map
            .Values
            .OrderBy(target => target.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
