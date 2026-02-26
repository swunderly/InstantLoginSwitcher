using System.Text;

namespace InstantLoginSwitcher.Core.Services;

public static class InstallPaths
{
    public const string RootOverrideEnvironmentVariable = "INSTANT_LOGIN_SWITCHER_ROOT";

    public static string RootDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                try
                {
                    return Path.GetFullPath(overridePath.Trim());
                }
                catch
                {
                    // Fall back to default path when override is invalid.
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "InstantLoginSwitcher");
        }
    }

    public static string ConfigPath => Path.Combine(RootDirectory, "config.json");
    public static string ConfigBackupPath => Path.Combine(RootDirectory, "config.backup.json");
    public static string ListenerLogPath => Path.Combine(RootDirectory, "listener.log");
    public static string SwitchLogPath => Path.Combine(RootDirectory, "switch.log");
    public static string PendingAutoLogonMarkerPath => Path.Combine(RootDirectory, "pending-autologon-cleanup.flag");

    public static void EnsureRootDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
    }

    public static void ClearRuntimeLogs()
    {
        EnsureRootDirectory();
        TryDelete(ListenerLogPath);
        TryDelete(SwitchLogPath);
    }

    public static void ClearPendingAutoLogonMarker()
    {
        EnsureRootDirectory();
        TryDelete(PendingAutoLogonMarkerPath);
    }

    public static void WriteUtf8NoBom(string path, string content)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(path, content, encoding);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort log cleanup.
        }
    }
}
