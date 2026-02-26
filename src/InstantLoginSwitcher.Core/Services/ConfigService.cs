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
        if (!File.Exists(InstallPaths.ConfigPath))
        {
            return NewEmptyConfig();
        }

        var json = File.ReadAllText(InstallPaths.ConfigPath);
        var config = JsonSerializer.Deserialize<SwitcherConfig>(json, SerializerOptions) ?? NewEmptyConfig();
        Normalize(config);
        return config;
    }

    public void Save(SwitcherConfig config)
    {
        Normalize(config);
        config.MachineName = Environment.MachineName;
        config.UpdatedAtUtc = DateTime.UtcNow.ToString("o");

        InstallPaths.EnsureRootDirectory();
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        InstallPaths.WriteUtf8NoBom(InstallPaths.ConfigPath, json + Environment.NewLine);
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
}
