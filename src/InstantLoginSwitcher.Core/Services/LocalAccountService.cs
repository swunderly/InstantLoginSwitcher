using System.Management;
using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.Core.Services;

public sealed class LocalAccountService
{
    private const string LocalAdministratorsSid = "S-1-5-32-544";

    public IReadOnlyList<LocalAdminAccount> GetEnabledLocalAdministrators()
    {
        var machine = Environment.MachineName;
        var result = new Dictionary<string, LocalAdminAccount>(StringComparer.OrdinalIgnoreCase);

        foreach (var groupPath in GetLocalAdministratorsGroupPaths())
        {
            foreach (var account in GetGroupMembers(groupPath, machine))
            {
                result[account.UserName] = account;
            }
        }

        return result.Values
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

    private static IReadOnlyList<string> GetLocalAdministratorsGroupPaths()
    {
        var query =
            $"SELECT __RELPATH FROM Win32_Group WHERE LocalAccount = TRUE AND SID = '{LocalAdministratorsSid}'";

        var paths = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject group in searcher.Get())
            {
                var relPath = ReadString(group, "__RELPATH");
                if (!string.IsNullOrWhiteSpace(relPath))
                {
                    paths.Add(relPath);
                }
            }
        }
        catch
        {
            // If WMI fails we return empty and UI will explain no accounts found.
        }

        return paths;
    }

    private static IReadOnlyList<LocalAdminAccount> GetGroupMembers(string groupPath, string machine)
    {
        var result = new List<LocalAdminAccount>();

        var query = $"ASSOCIATORS OF {{{groupPath}}} WHERE AssocClass = Win32_GroupUser Role = GroupComponent";
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject member in searcher.Get())
            {
                if (!member.ClassPath.ClassName.Equals("Win32_UserAccount", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ReadBool(member, "LocalAccount"))
                {
                    continue;
                }

                if (ReadBool(member, "Disabled"))
                {
                    continue;
                }

                var userName = ReadString(member, "Name");
                if (string.IsNullOrWhiteSpace(userName))
                {
                    continue;
                }

                var fullName = ReadString(member, "FullName");
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = userName;
                }

                var sidValue = ReadString(member, "SID");
                result.Add(new LocalAdminAccount
                {
                    UserName = userName,
                    FullName = fullName,
                    Qualified = $"{machine}\\{userName}",
                    SidValue = sidValue
                });
            }
        }
        catch
        {
            // Ignore broken member enumeration and return best-effort results.
        }

        return result;
    }

    private static string ReadString(ManagementBaseObject source, string propertyName)
    {
        var value = source[propertyName];
        return value?.ToString() ?? string.Empty;
    }

    private static bool ReadBool(ManagementBaseObject source, string propertyName)
    {
        var value = source[propertyName];
        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is null)
        {
            return false;
        }

        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }
}
