using System.Text;

namespace InstantLoginSwitcher.Core.Services;

public static class FileLogger
{
    public static void WriteLine(string logPath, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Logging should never break hotkey execution.
        }
    }
}
