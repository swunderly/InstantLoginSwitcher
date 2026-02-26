using System.Text.Json;
using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.Core.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SwitcherConfig Load()
    {
        InstallPaths.EnsureRootDirectory();
        if (TryLoadFromPath(InstallPaths.ConfigPath, out var primary))
        {
            return primary;
        }

        if (TryLoadFromPath(InstallPaths.ConfigBackupPath, out var backup))
        {
            TryRestorePrimaryFromBackup();
            return backup;
        }

        return NewEmptyConfig();
    }

    public void Save(SwitcherConfig config)
    {
        Normalize(config);
        config.MachineName = Environment.MachineName;
        config.UpdatedAtUtc = DateTime.UtcNow.ToString("o");

        InstallPaths.EnsureRootDirectory();
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var tempPath = InstallPaths.ConfigPath + ".tmp";
        InstallPaths.WriteUtf8NoBom(tempPath, json + Environment.NewLine);

        try
        {
            if (File.Exists(InstallPaths.ConfigPath))
            {
                try
                {
                    File.Replace(tempPath, InstallPaths.ConfigPath, InstallPaths.ConfigBackupPath, ignoreMetadataErrors: true);
                }
                catch
                {
                    File.Copy(tempPath, InstallPaths.ConfigPath, overwrite: true);
                    File.Copy(InstallPaths.ConfigPath, InstallPaths.ConfigBackupPath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, InstallPaths.ConfigPath, overwrite: true);
                File.Copy(InstallPaths.ConfigPath, InstallPaths.ConfigBackupPath, overwrite: true);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static SwitcherConfig NewEmptyConfig()
    {
        return new SwitcherConfig
        {
            Version = 1,
            MachineName = Environment.MachineName,
            UpdatedAtUtc = DateTime.UtcNow.ToString("o"),
            Users = new List<StoredUserCredential>(),
            Profiles = new List<SwitchProfile>()
        };
    }

    private static void Normalize(SwitcherConfig config)
    {
        config.Version = Math.Max(config.Version, 1);
        config.Users ??= new List<StoredUserCredential>();
        config.Profiles ??= new List<SwitchProfile>();

        foreach (var profile in config.Profiles)
        {
            profile.Name ??= string.Empty;
            profile.UserA ??= string.Empty;
            profile.UserB ??= string.Empty;
            profile.Hotkey ??= string.Empty;
            if (profile.Id == Guid.Empty)
            {
                profile.Id = Guid.NewGuid();
            }
        }

        foreach (var user in config.Users)
        {
            user.UserName ??= string.Empty;
            user.FullName ??= string.Empty;
            user.Qualified ??= string.Empty;
            user.SidValue ??= string.Empty;
            user.PasswordEncrypted ??= string.Empty;
            user.PicturePath ??= string.Empty;
        }
    }

    private static bool TryLoadFromPath(string path, out SwitcherConfig config)
    {
        config = NewEmptyConfig();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<SwitcherConfig>(json, SerializerOptions);
            if (parsed is null)
            {
                return false;
            }

            Normalize(parsed);
            config = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryRestorePrimaryFromBackup()
    {
        try
        {
            if (!File.Exists(InstallPaths.ConfigBackupPath))
            {
                return;
            }

            File.Copy(InstallPaths.ConfigBackupPath, InstallPaths.ConfigPath, overwrite: true);
        }
        catch
        {
            // Best-effort restore only.
        }
    }

    private static void TryDeleteFile(string path)
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
            // Best-effort cleanup only.
        }
    }
}
