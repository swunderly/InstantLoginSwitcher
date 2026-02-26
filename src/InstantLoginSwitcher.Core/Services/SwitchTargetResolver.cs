using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.Core.Services;

public sealed class SwitchTargetResolver
{
    public IReadOnlyList<SwitchTarget> ResolveTargets(SwitcherConfig config, string hotkeyCanonical, string currentUser)
    {
        var hotkeyParser = new HotkeyParser();
        var normalizedCurrentUser = currentUser?.Trim() ?? string.Empty;
        var map = new Dictionary<string, SwitchTarget>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in config.Profiles.Where(profile => profile.Enabled))
        {
            var userA = profile.UserA?.Trim() ?? string.Empty;
            var userB = profile.UserB?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userA) ||
                string.IsNullOrWhiteSpace(userB) ||
                userA.Equals(userB, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string parsedProfileHotkey;
            try
            {
                parsedProfileHotkey = hotkeyParser.Parse(profile.Hotkey).CanonicalText;
            }
            catch
            {
                continue;
            }

            if (!parsedProfileHotkey.Equals(hotkeyCanonical, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? targetUser = null;
            if (string.Equals(userA, normalizedCurrentUser, StringComparison.OrdinalIgnoreCase))
            {
                targetUser = userB;
            }
            else if (string.Equals(userB, normalizedCurrentUser, StringComparison.OrdinalIgnoreCase))
            {
                targetUser = userA;
            }

            if (string.IsNullOrWhiteSpace(targetUser) || map.ContainsKey(targetUser))
            {
                continue;
            }

            var credential = config.Users.FirstOrDefault(user =>
                string.Equals(user.UserName?.Trim(), targetUser, StringComparison.OrdinalIgnoreCase));
            if (credential is null)
            {
                continue;
            }

            map[targetUser] = new SwitchTarget
            {
                UserName = targetUser,
                FullName = string.IsNullOrWhiteSpace(credential.FullName) ? targetUser : credential.FullName,
                Qualified = string.IsNullOrWhiteSpace(credential.Qualified)
                    ? $"{Environment.MachineName}\\{targetUser}"
                    : credential.Qualified,
                PicturePath = credential.PicturePath ?? string.Empty
            };
        }

        return map
            .Values
            .OrderBy(target => target.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
