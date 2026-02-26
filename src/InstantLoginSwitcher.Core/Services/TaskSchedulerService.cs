using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace InstantLoginSwitcher.Core.Services;

public sealed class TaskSchedulerService
{
    public const string TaskPrefix = "InstantLoginSwitcher-Hotkey-";
    public const string LegacyTaskName = "InstantLoginSwitcher-Hotkey-Listener";

    public void SyncListenerTasks(IEnumerable<string> requiredUsers, string listenerExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(listenerExecutablePath))
        {
            throw new InvalidOperationException("Listener executable path is empty.");
        }

        if (!File.Exists(listenerExecutablePath))
        {
            throw new InvalidOperationException($"Listener executable not found: {listenerExecutablePath}");
        }

        var required = new HashSet<string>(requiredUsers.Where(user => !string.IsNullOrWhiteSpace(user)), StringComparer.OrdinalIgnoreCase);
        var desiredTaskNames = required.Select(GetTaskNameForUser).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existingTask in GetManagedTaskNames())
        {
            if (!desiredTaskNames.Contains(existingTask))
            {
                RemoveTask(existingTask);
            }
        }

        foreach (var user in required.OrderBy(user => user, StringComparer.OrdinalIgnoreCase))
        {
            CreateOrUpdateTask(user, listenerExecutablePath);
        }
    }

    public void StartListenerForUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        var taskName = GetTaskNameForUser(userName);
        RunSchtasks(
            ["/Run", "/TN", taskName],
            allowFailure: true);
    }

    public void RemoveAllManagedTasks()
    {
        foreach (var taskName in GetManagedTaskNames())
        {
            RemoveTask(taskName);
        }
    }

    public IReadOnlyList<string> GetManagedTaskNamesForDiagnostics()
    {
        return GetManagedTaskNames();
    }

    public string GetTaskNameForUser(string userName)
    {
        var normalized = userName.Trim();
        var sanitized = new string(normalized
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new InvalidOperationException("User name cannot be blank when creating a task name.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..8];
        return $"{TaskPrefix}{sanitized}_{hash}";
    }

    private void CreateOrUpdateTask(string userName, string listenerExecutablePath)
    {
        var taskName = GetTaskNameForUser(userName);
        var qualifiedUser = $"{Environment.MachineName}\\{userName}";
        var taskRun = $"\"{listenerExecutablePath}\" --listener";

        RunSchtasks(
            [
                "/Create",
                "/TN", taskName,
                "/SC", "ONLOGON",
                "/RU", qualifiedUser,
                "/RL", "HIGHEST",
                "/IT",
                "/F",
                "/TR", taskRun
            ],
            allowFailure: false);
    }

    private void RemoveTask(string taskName)
    {
        RunSchtasks(
            ["/Delete", "/TN", taskName, "/F"],
            allowFailure: true);
    }

    private IReadOnlyList<string> GetManagedTaskNames()
    {
        var output = RunSchtasks(
            ["/Query", "/FO", "CSV", "/NH"],
            allowFailure: false);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var firstField = ParseFirstCsvField(line);
            if (string.IsNullOrWhiteSpace(firstField))
            {
                continue;
            }

            var normalized = firstField.Trim().TrimStart('\\');
            if (normalized.StartsWith(TaskPrefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(LegacyTaskName, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(normalized);
            }
        }

        return result.ToList();
    }

    private static string ParseFirstCsvField(string csvLine)
    {
        if (string.IsNullOrWhiteSpace(csvLine))
        {
            return string.Empty;
        }

        if (!csvLine.StartsWith('"'))
        {
            var commaIndex = csvLine.IndexOf(',');
            return commaIndex < 0 ? csvLine : csvLine[..commaIndex];
        }

        var builder = new StringBuilder();
        for (var i = 1; i < csvLine.Length; i++)
        {
            var current = csvLine[i];
            if (current == '"')
            {
                var nextIsQuote = i + 1 < csvLine.Length && csvLine[i + 1] == '"';
                if (nextIsQuote)
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                break;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string RunSchtasks(IReadOnlyList<string> arguments, bool allowFailure)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start schtasks.exe.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(
                $"schtasks failed (exit {process.ExitCode}) with arguments [{string.Join(" ", arguments)}]\n{output}\n{error}");
        }

        return output;
    }
}
