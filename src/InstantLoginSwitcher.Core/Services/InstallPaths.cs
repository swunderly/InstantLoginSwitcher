using System.Text;

namespace InstantLoginSwitcher.Core.Services;

public static class InstallPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "InstantLoginSwitcher");

    public static string ConfigPath => Path.Combine(RootDirectory, "config.json");
    public static string ListenerLogPath => Path.Combine(RootDirectory, "listener.log");
    public static string SwitchLogPath => Path.Combine(RootDirectory, "switch.log");

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
