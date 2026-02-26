using System.Collections;
using System.DirectoryServices;
using System.Globalization;
using System.Reflection;
using System.Security.Principal;
using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.Core.Services;

public sealed class LocalAccountService
{
    private const int AccountDisableFlag = 0x0002;

    public IReadOnlyList<LocalAdminAccount> GetEnabledLocalAdministrators()
    {
        var machine = Environment.MachineName;
        var expectedPrefix = $"WinNT://{machine}/";
        var result = new List<LocalAdminAccount>();

        using var administrators = new DirectoryEntry($"WinNT://{machine}/Administrators,group");
        foreach (var memberObject in (IEnumerable)administrators.Invoke("Members"))
        {
            var memberPath = TryGetAdsPath(memberObject);
            if (string.IsNullOrWhiteSpace(memberPath))
            {
                continue;
            }

            using var member = new DirectoryEntry(memberPath);

            if (!string.Equals(member.SchemaClassName, "User", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!member.Path.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var userName = ReadStringProperty(member, "Name");
            if (string.IsNullOrWhiteSpace(userName))
            {
                continue;
            }

            if (IsDisabled(member))
            {
                continue;
            }

            var fullName = ReadStringProperty(member, "FullName");
            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = userName;
            }

            result.Add(new LocalAdminAccount
            {
                UserName = userName,
                FullName = fullName,
                Qualified = $"{machine}\\{userName}",
                SidValue = ReadSid(member)
            });
        }

        return result
            .OrderBy(entry => entry.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public LocalAdminAccount? FindByUserName(IEnumerable<LocalAdminAccount> accounts, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var candidate = input.Trim();
        if (candidate.Contains('\\'))
        {
            candidate = candidate.Split('\\', 2)[1];
        }

        return accounts.FirstOrDefault(account =>
            account.UserName.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            account.FullName.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDisabled(DirectoryEntry member)
    {
        var flags = member.Properties["UserFlags"]?.Value;
        if (flags is null)
        {
            return false;
        }

        var value = Convert.ToInt32(flags, CultureInfo.InvariantCulture);
        return (value & AccountDisableFlag) != 0;
    }

    private static string ReadStringProperty(DirectoryEntry entry, string propertyName)
    {
        var value = entry.Properties[propertyName]?.Value;
        return value is null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string ReadSid(DirectoryEntry entry)
    {
        try
        {
            if (entry.Properties["objectSid"]?.Value is byte[] sidBytes)
            {
                return new SecurityIdentifier(sidBytes, 0).Value;
            }
        }
        catch
        {
            // SID is optional in UI context.
        }

        return string.Empty;
    }

    private static string TryGetAdsPath(object memberObject)
    {
        try
        {
            var path = memberObject.GetType().InvokeMember(
                "ADsPath",
                BindingFlags.GetProperty,
                binder: null,
                target: memberObject,
                args: null);

            return path is null ? string.Empty : Convert.ToString(path, CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
