using System.Diagnostics;
using InstantLoginSwitcher.Core.Models;
using Microsoft.Win32;

namespace InstantLoginSwitcher.Core.Services;

public sealed class SwitchExecutor
{
    private readonly PasswordProtector _passwordProtector;

    public SwitchExecutor(PasswordProtector passwordProtector)
    {
        _passwordProtector = passwordProtector;
    }

    public void ExecuteSwitch(SwitcherConfig config, string hotkeyCanonical, string currentUser, string targetUser)
    {
        var (credential, password) = ResolveUsableCredential(config, targetUser);

        ConfigureWinlogon(credential, password);
        MarkPendingAutoLogonCleanup(targetUser, currentUser, hotkeyCanonical);

        FileLogger.WriteLine(
            InstallPaths.SwitchLogPath,
            $"Prepared auto sign-in for '{targetUser}' (triggered by '{currentUser}', hotkey '{hotkeyCanonical}').");

        StartLogoff();
    }

    private (StoredUserCredential Credential, string Password) ResolveUsableCredential(SwitcherConfig config, string targetUser)
    {
        var normalizedTargetUser = targetUser?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTargetUser))
        {
            throw new InvalidOperationException("Target user is not valid.");
        }

        var candidates = config.Users
            .Where(user =>
                !string.IsNullOrWhiteSpace(user.UserName) &&
                string.Equals(user.UserName.Trim(), normalizedTargetUser, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"No credential is configured for target user '{normalizedTargetUser}'.");
        }

        var failures = new List<string>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.PasswordEncrypted))
            {
                failures.Add("empty encrypted value");
                continue;
            }

            try
            {
                var password = _passwordProtector.Unprotect(candidate.PasswordEncrypted);
                if (string.IsNullOrWhiteSpace(password))
                {
                    failures.Add("blank decrypted value");
                    continue;
                }

                return (candidate, password);
            }
            catch (Exception exception)
            {
                failures.Add("decrypt failed: " + ToSingleLine(exception.Message, maxLength: 120));
            }
        }

        var failureSuffix = failures.Count == 0
            ? string.Empty
            : $" Details: {string.Join(" | ", failures.Take(3))}";
        throw new InvalidOperationException(
            $"No usable credential was found for '{normalizedTargetUser}'. Re-enter passwords for this profile and save again.{failureSuffix}");
    }

    private static void ConfigureWinlogon(StoredUserCredential target, string password)
    {
        using var winlogon = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
            writable: true);

        if (winlogon is null)
        {
            throw new InvalidOperationException("Cannot open Winlogon registry path for writing.");
        }

        winlogon.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
        winlogon.SetValue("ForceAutoLogon", "1", RegistryValueKind.String);
        winlogon.SetValue("DefaultUserName", target.UserName, RegistryValueKind.String);
        winlogon.SetValue("DefaultPassword", password, RegistryValueKind.String);
        winlogon.SetValue("DefaultDomainName", Environment.MachineName, RegistryValueKind.String);
        winlogon.SetValue("AltDefaultUserName", target.UserName, RegistryValueKind.String);
        winlogon.SetValue("AltDefaultDomainName", Environment.MachineName, RegistryValueKind.String);
        winlogon.SetValue("LastUsedUsername", target.Qualified, RegistryValueKind.String);
        winlogon.SetValue("AutoLogonCount", "1", RegistryValueKind.String);

        if (!string.IsNullOrWhiteSpace(target.SidValue))
        {
            winlogon.SetValue("AutoLogonSID", target.SidValue, RegistryValueKind.String);
        }
        else
        {
            TryDeleteValue(winlogon, "AutoLogonSID");
        }

    }

    public void DisableAutoLogon()
    {
        using var winlogon = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
            writable: true);

        if (winlogon is null)
        {
            return;
        }

        winlogon.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
        winlogon.SetValue("ForceAutoLogon", "0", RegistryValueKind.String);
        TryDeleteValue(winlogon, "DefaultPassword");
        TryDeleteValue(winlogon, "DefaultUserName");
        TryDeleteValue(winlogon, "DefaultDomainName");
        TryDeleteValue(winlogon, "AltDefaultUserName");
        TryDeleteValue(winlogon, "AltDefaultDomainName");
        TryDeleteValue(winlogon, "AutoLogonSID");
        TryDeleteValue(winlogon, "AutoLogonCount");
        InstallPaths.ClearPendingAutoLogonMarker();
    }

    public void TryClearPendingAutoLogon()
    {
        try
        {
            if (!File.Exists(InstallPaths.PendingAutoLogonMarkerPath))
            {
                return;
            }

            DisableAutoLogon();
            FileLogger.WriteLine(InstallPaths.SwitchLogPath, "Cleared pending auto-logon state after sign-in.");
        }
        catch (Exception exception)
        {
            FileLogger.WriteLine(InstallPaths.SwitchLogPath, "Auto-logon cleanup failed: " + exception.Message);
        }
    }

    private static void StartLogoff()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/l /f",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        process.Start();
    }

    private static void MarkPendingAutoLogonCleanup(string targetUser, string currentUser, string hotkeyCanonical)
    {
        try
        {
            InstallPaths.EnsureRootDirectory();
            var markerText =
                $"{DateTime.UtcNow:o}|from={currentUser}|to={targetUser}|hotkey={hotkeyCanonical}{Environment.NewLine}";
            InstallPaths.WriteUtf8NoBom(InstallPaths.PendingAutoLogonMarkerPath, markerText);
        }
        catch
        {
            // Marker write is best-effort. Missing marker only skips auto cleanup.
        }
    }

    private static void TryDeleteValue(RegistryKey key, string name)
    {
        try
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static string ToSingleLine(string? text, int maxLength)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length <= maxLength || maxLength <= 3)
        {
            return normalized;
        }

        return normalized[..(maxLength - 3)] + "...";
    }
}
