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
        var credential = config.Users.FirstOrDefault(user =>
            user.UserName.Equals(targetUser, StringComparison.OrdinalIgnoreCase));

        if (credential is null)
        {
            throw new InvalidOperationException($"No credential is configured for target user '{targetUser}'.");
        }

        var password = _passwordProtector.Unprotect(credential.PasswordEncrypted);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException($"Credential for '{targetUser}' is empty or invalid.");
        }

        ConfigureWinlogon(credential, password);

        FileLogger.WriteLine(
            InstallPaths.SwitchLogPath,
            $"Prepared auto sign-in for '{targetUser}' (triggered by '{currentUser}', hotkey '{hotkeyCanonical}').");

        StartLogoff();
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

        if (!string.IsNullOrWhiteSpace(target.SidValue))
        {
            winlogon.SetValue("AutoLogonSID", target.SidValue, RegistryValueKind.String);
        }
        else
        {
            TryDeleteValue(winlogon, "AutoLogonSID");
        }

        TryDeleteValue(winlogon, "AutoLogonCount");
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
}
