using InstantLoginSwitcher.Core.Models;
using InstantLoginSwitcher.Core.Services;

namespace InstantLoginSwitcher.Core.Tests;

public sealed class ConfigServiceTests
{
    private static readonly object EnvLock = new();

    [Fact]
    public void Save_CreatesPrimaryAndBackupConfig()
    {
        lock (EnvLock)
        {
            using var scope = new RootOverrideScope();
            var service = new ConfigService();
            var config = CreateSampleConfig();

            service.Save(config);

            Assert.True(File.Exists(InstallPaths.ConfigPath));
            Assert.True(File.Exists(InstallPaths.ConfigBackupPath));
        }
    }

    [Fact]
    public void Load_FallsBackToBackupWhenPrimaryIsCorrupt()
    {
        lock (EnvLock)
        {
            using var scope = new RootOverrideScope();
            var service = new ConfigService();
            var source = CreateSampleConfig();

            service.Save(source);
            var backupText = File.ReadAllText(InstallPaths.ConfigBackupPath);
            File.WriteAllText(InstallPaths.ConfigPath, "{ this is not valid json ");

            var loaded = service.Load();

            Assert.Single(loaded.Profiles);
            Assert.Equal("Profile A", loaded.Profiles[0].Name);
            var restoredText = File.ReadAllText(InstallPaths.ConfigPath);
            Assert.Equal(backupText, restoredText);
        }
    }

    private static SwitcherConfig CreateSampleConfig()
    {
        return new SwitcherConfig
        {
            Profiles =
            [
                new SwitchProfile
                {
                    Name = "Profile A",
                    UserA = "alice",
                    UserB = "bob",
                    Hotkey = "Ctrl+Alt+S",
                    Enabled = true
                }
            ],
            Users =
            [
                new StoredUserCredential
                {
                    UserName = "alice",
                    FullName = "Alice",
                    Qualified = "PC\\alice",
                    SidValue = "S-1-5-21-1",
                    PasswordEncrypted = "enc-a"
                },
                new StoredUserCredential
                {
                    UserName = "bob",
                    FullName = "Bob",
                    Qualified = "PC\\bob",
                    SidValue = "S-1-5-21-2",
                    PasswordEncrypted = "enc-b"
                }
            ]
        };
    }

    private sealed class RootOverrideScope : IDisposable
    {
        private readonly string? _previousValue;
        private readonly string _tempDirectory;

        public RootOverrideScope()
        {
            _previousValue = Environment.GetEnvironmentVariable(InstallPaths.RootOverrideEnvironmentVariable);
            _tempDirectory = Path.Combine(Path.GetTempPath(), "InstantLoginSwitcher.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            Environment.SetEnvironmentVariable(InstallPaths.RootOverrideEnvironmentVariable, _tempDirectory);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(InstallPaths.RootOverrideEnvironmentVariable, _previousValue);
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
