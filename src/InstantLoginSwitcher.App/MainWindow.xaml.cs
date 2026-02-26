using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using InstantLoginSwitcher.App.Models;
using InstantLoginSwitcher.App.Views;
using InstantLoginSwitcher.Core.Models;
using InstantLoginSwitcher.Core.Services;

namespace InstantLoginSwitcher.App;

public partial class MainWindow : Window
{
    private const string DefaultHotkey = "Numpad4+Numpad6";

    private readonly ConfigService _configService;
    private readonly HotkeyParser _hotkeyParser;
    private readonly PasswordProtector _passwordProtector;
    private readonly CredentialValidator _credentialValidator;
    private readonly LocalAccountService _localAccountService;
    private readonly AccountPictureService _accountPictureService;
    private readonly TaskSchedulerService _taskSchedulerService;
    private readonly SwitchExecutor _switchExecutor;

    private readonly ObservableCollection<ProfileEditorModel> _profiles = new();
    private List<AccountOption> _accountOptions = new();
    private SwitcherConfig _loadedConfig = new();
    private Guid? _editingProfileId;
    private bool _hasUnsavedChanges;
    private bool _hasDraftChanges;
    private int _dirtyTrackingSuspendDepth;

    public MainWindow(
        ConfigService configService,
        HotkeyParser hotkeyParser,
        PasswordProtector passwordProtector,
        CredentialValidator credentialValidator,
        LocalAccountService localAccountService,
        AccountPictureService accountPictureService,
        TaskSchedulerService taskSchedulerService,
        SwitchExecutor switchExecutor)
    {
        _configService = configService;
        _hotkeyParser = hotkeyParser;
        _passwordProtector = passwordProtector;
        _credentialValidator = credentialValidator;
        _localAccountService = localAccountService;
        _accountPictureService = accountPictureService;
        _taskSchedulerService = taskSchedulerService;
        _switchExecutor = switchExecutor;

        InitializeComponent();
        Closing += MainWindow_Closing;
        ProfilesGrid.ItemsSource = _profiles;

        ReloadState();
    }

    private void ReloadState()
    {
        try
        {
            var loadedConfig = _configService.Load();
            var accountOptions = _localAccountService
                .GetEnabledLocalAdministrators()
                .Select(account => new AccountOption(account))
                .ToList();

            RunWithoutDirtyTracking(() =>
            {
                _loadedConfig = loadedConfig;
                _accountOptions = accountOptions;

                UserACombo.ItemsSource = _accountOptions;
                UserBCombo.ItemsSource = _accountOptions;
                UserACombo.DisplayMemberPath = nameof(AccountOption.Label);
                UserBCombo.DisplayMemberPath = nameof(AccountOption.Label);

                AccountsSummaryText.Text = _accountOptions.Count switch
                {
                    0 => "No local administrator accounts were found.",
                    1 => "Only one local administrator account found. At least two are required.",
                    _ => $"Available local administrator accounts: {string.Join(", ", _accountOptions.Select(option => option.Account.UserName))}"
                };

                _profiles.Clear();
                foreach (var profile in _loadedConfig.Profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _profiles.Add(new ProfileEditorModel
                    {
                        Id = profile.Id,
                        Name = profile.Name,
                        UserA = profile.UserA,
                        UserB = profile.UserB,
                        Hotkey = profile.Hotkey,
                        Enabled = profile.Enabled
                    });
                }

                ClearFormInternal();
            });

            _hasUnsavedChanges = false;
            _hasDraftChanges = false;
            SetStatus("Configuration loaded.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to load configuration: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddOrUpdateProfile_Click(object sender, RoutedEventArgs e)
    {
        var firstUser = GetSelectedAccount(UserACombo);
        var secondUser = GetSelectedAccount(UserBCombo);

        if (firstUser is null || secondUser is null)
        {
            MessageBox.Show(this, "Select both users.", "InstantLoginSwitcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (firstUser.UserName.Equals(secondUser.UserName, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "First user and second user must be different.", "InstantLoginSwitcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HotkeyDefinition definition;
        try
        {
            definition = _hotkeyParser.Parse(HotkeyBox.Text);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = ProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{firstUser.UserName} <-> {secondUser.UserName}";
        }

        if (HasDuplicateProfile(firstUser.UserName, secondUser.UserName, definition.CanonicalText, _editingProfileId))
        {
            MessageBox.Show(
                this,
                "A profile with the same two users and hotkey already exists.",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_editingProfileId is Guid existingId)
        {
            var existing = _profiles.FirstOrDefault(profile => profile.Id == existingId);
            if (existing is null)
            {
                _editingProfileId = null;
                AddOrUpdateProfile_Click(sender, e);
                return;
            }

            existing.Name = name;
            existing.UserA = firstUser.UserName;
            existing.UserB = secondUser.UserName;
            existing.Hotkey = definition.CanonicalText;
            existing.Enabled = EnabledCheck.IsChecked ?? true;

            RefreshProfileGrid();
            _hasUnsavedChanges = true;
            _hasDraftChanges = false;
            SetStatus("Profile updated. Remember to click Save And Apply.");
        }
        else
        {
            _profiles.Add(new ProfileEditorModel
            {
                Id = Guid.NewGuid(),
                Name = name,
                UserA = firstUser.UserName,
                UserB = secondUser.UserName,
                Hotkey = definition.CanonicalText,
                Enabled = EnabledCheck.IsChecked ?? true
            });

            _hasUnsavedChanges = true;
            _hasDraftChanges = false;
            SetStatus("Profile added. Remember to click Save And Apply.");
        }

        ClearFormInternal();
    }

    private void RemoveSelectedProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileEditorModel selected)
        {
            MessageBox.Show(this, "Select a profile to remove.", "InstantLoginSwitcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var decision = MessageBox.Show(
            this,
            $"Remove profile '{selected.Name}'?",
            "InstantLoginSwitcher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        _profiles.Remove(selected);
        ClearFormInternal();
        _hasUnsavedChanges = true;
        SetStatus("Profile removed. Click Save And Apply to persist.");
    }

    private void UpdatePasswordsForSelectedProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileEditorModel selected)
        {
            MessageBox.Show(this, "Select a profile first.", "InstantLoginSwitcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var accountByUser = _accountOptions
                .GroupBy(option => option.Account.UserName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Account, StringComparer.OrdinalIgnoreCase);

            foreach (var userName in new[] { selected.UserA, selected.UserB }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!accountByUser.TryGetValue(userName, out var account))
                {
                    throw new InvalidOperationException($"Could not find local administrator account '{userName}'.");
                }

                var updatedCredential = CreateCredentialFromPrompt(account);
                UpsertCredential(_loadedConfig.Users, updatedCredential);
            }

            _configService.Save(_loadedConfig);
            SetStatus("Passwords updated for the selected profile users.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Password update failed: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveAndApply_Click(object sender, RoutedEventArgs e)
    {
        if (_hasDraftChanges)
        {
            var continueWithoutDraft = MessageBox.Show(
                this,
                "You changed values in the profile form but did not click Add Profile or Update Profile. Save and apply without those form edits?",
                "InstantLoginSwitcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (continueWithoutDraft != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            var configToSave = BuildConfigFromUi();
            _configService.Save(configToSave);

            var requiredUsers = configToSave.Profiles
                .Where(profile => profile.Enabled)
                .SelectMany(profile => new[] { profile.UserA, profile.UserB })
                .Where(user => !string.IsNullOrWhiteSpace(user))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentExecutable = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExecutable))
            {
                throw new InvalidOperationException("Could not resolve application executable path for listener startup task.");
            }

            _taskSchedulerService.SyncListenerTasks(requiredUsers, currentExecutable);
            var currentUserIncluded = requiredUsers.Any(user =>
                user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase));
            if (requiredUsers.Count == 0)
            {
                _switchExecutor.DisableAutoLogon();
            }
            else if (currentUserIncluded)
            {
                _taskSchedulerService.StartListenerForUser(Environment.UserName);
            }

            _loadedConfig = configToSave;
            _hasUnsavedChanges = false;
            _hasDraftChanges = false;

            var saveStatus = requiredUsers.Count == 0
                ? "Configuration saved. No profiles enabled, startup tasks removed."
                : currentUserIncluded
                    ? "Configuration saved and startup tasks updated."
                    : "Configuration saved. Current user is not in any enabled profile, so no listener was started for this account.";
            SetStatus(saveStatus);

            MessageBox.Show(
                this,
                requiredUsers.Count == 0
                    ? "Saved successfully. No active profiles remain, and startup tasks were removed."
                    : currentUserIncluded
                        ? "Saved successfully. Hotkey profiles are now active for configured users."
                        : "Saved successfully. Startup tasks were updated, but this signed-in account is not part of an enabled profile.",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Save failed: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        ReloadState();
    }

    private void RepairStartupTasks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var requiredUsers = _profiles
                .Where(profile => profile.Enabled)
                .SelectMany(profile => new[] { profile.UserA, profile.UserB })
                .Where(user => !string.IsNullOrWhiteSpace(user))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentExecutable = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExecutable))
            {
                throw new InvalidOperationException("Could not resolve executable path.");
            }

            _taskSchedulerService.SyncListenerTasks(requiredUsers, currentExecutable);
            var currentUserIncluded = requiredUsers.Any(user =>
                user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase));
            if (requiredUsers.Count == 0)
            {
                _switchExecutor.DisableAutoLogon();
                SetStatus("No active profiles found. Startup tasks removed and auto-logon values cleared.");
            }
            else if (currentUserIncluded)
            {
                _taskSchedulerService.StartListenerForUser(Environment.UserName);
                SetStatus("Startup tasks repaired for configured profiles.");
            }
            else
            {
                SetStatus("Startup tasks repaired. Current user is not in an enabled profile, so listener was not started for this account.");
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Task repair failed: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearForm_Click(object sender, RoutedEventArgs e)
    {
        ClearFormInternal();
    }

    private void RemoveAllTasks_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            "Remove all InstantLoginSwitcher startup tasks and clear auto logon registry values?",
            "InstantLoginSwitcher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _taskSchedulerService.RemoveAllManagedTasks();
            _switchExecutor.DisableAutoLogon();
            SetStatus("All startup tasks removed and auto-logon values cleared.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "InstantLoginSwitcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProfilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesGrid.SelectedItem is not ProfileEditorModel selected)
        {
            return;
        }

        RunWithoutDirtyTracking(() =>
        {
            _editingProfileId = selected.Id;
            ProfileNameBox.Text = selected.Name;
            HotkeyBox.Text = selected.Hotkey;
            EnabledCheck.IsChecked = selected.Enabled;
            UserACombo.SelectedItem = _accountOptions.FirstOrDefault(option =>
                option.Account.UserName.Equals(selected.UserA, StringComparison.OrdinalIgnoreCase));
            UserBCombo.SelectedItem = _accountOptions.FirstOrDefault(option =>
                option.Account.UserName.Equals(selected.UserB, StringComparison.OrdinalIgnoreCase));
        });

        _hasDraftChanges = false;
        AddOrUpdateButton.Content = "Update Profile";
        SetStatus("Editing selected profile.");
    }

    private SwitcherConfig BuildConfigFromUi()
    {
        var accountByUser = _accountOptions
            .GroupBy(option => option.Account.UserName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Account, StringComparer.OrdinalIgnoreCase);

        var profileList = new List<SwitchProfile>();
        var duplicateGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in _profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.UserA) || string.IsNullOrWhiteSpace(profile.UserB))
            {
                throw new InvalidOperationException("Each profile must include both users.");
            }

            if (profile.UserA.Equals(profile.UserB, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Profile '{profile.Name}' has identical users.");
            }

            var parsedHotkey = _hotkeyParser.Parse(profile.Hotkey);
            var orderedUsers = new[] { profile.UserA.Trim(), profile.UserB.Trim() }
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var signature = $"{orderedUsers[0]}|{orderedUsers[1]}|{parsedHotkey.CanonicalText}";
            if (!duplicateGuard.Add(signature))
            {
                throw new InvalidOperationException(
                    $"Duplicate profile detected for users '{orderedUsers[0]}' and '{orderedUsers[1]}' on hotkey '{parsedHotkey.CanonicalText}'.");
            }

            profileList.Add(new SwitchProfile
            {
                Id = profile.Id == Guid.Empty ? Guid.NewGuid() : profile.Id,
                Name = profile.Name.Trim(),
                UserA = profile.UserA.Trim(),
                UserB = profile.UserB.Trim(),
                Hotkey = parsedHotkey.CanonicalText,
                Enabled = profile.Enabled
            });
        }

        var requiredUsers = profileList
            .Where(profile => profile.Enabled)
            .SelectMany(profile => new[] { profile.UserA, profile.UserB })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingCredentials = _loadedConfig.Users
            .GroupBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var selectedCredentials = new List<StoredUserCredential>();
        foreach (var userName in requiredUsers.OrderBy(user => user, StringComparer.OrdinalIgnoreCase))
        {
            if (!accountByUser.TryGetValue(userName, out var account))
            {
                throw new InvalidOperationException($"Configured user '{userName}' is not an enabled local administrator.");
            }

            if (existingCredentials.TryGetValue(userName, out var existing) &&
                !string.IsNullOrWhiteSpace(existing.PasswordEncrypted))
            {
                existing.FullName = account.FullName;
                existing.Qualified = account.Qualified;
                existing.SidValue = account.SidValue;
                selectedCredentials.Add(existing);
                continue;
            }

            selectedCredentials.Add(CreateCredentialFromPrompt(account));
        }

        return new SwitcherConfig
        {
            Version = Math.Max(_loadedConfig.Version, 1),
            MachineName = Environment.MachineName,
            UpdatedAtUtc = DateTime.UtcNow.ToString("o"),
            Profiles = profileList,
            Users = selectedCredentials
        };
    }

    private string PromptForPassword(LocalAdminAccount account)
    {
        var prompt = new PasswordPromptWindow(account.Qualified)
        {
            Owner = this
        };

        var result = prompt.ShowDialog();
        if (result != true)
        {
            throw new InvalidOperationException($"Password entry cancelled for {account.Qualified}.");
        }

        if (string.IsNullOrWhiteSpace(prompt.Password))
        {
            throw new InvalidOperationException($"Password for {account.Qualified} cannot be blank.");
        }

        return prompt.Password;
    }

    private StoredUserCredential CreateCredentialFromPrompt(LocalAdminAccount account)
    {
        var password = PromptForPassword(account);
        var result = _credentialValidator.Validate(account.Qualified, password);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Password validation failed for {account.Qualified} (Win32 error {result.Win32Error}). Use the Windows account password, not a PIN.");
        }

        return new StoredUserCredential
        {
            UserName = account.UserName,
            FullName = account.FullName,
            Qualified = account.Qualified,
            SidValue = account.SidValue,
            PasswordEncrypted = _passwordProtector.Protect(password),
            PicturePath = _accountPictureService.GetPicturePath(account)
        };
    }

    private LocalAdminAccount? GetSelectedAccount(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as AccountOption)?.Account;
    }

    private void ClearFormInternal()
    {
        RunWithoutDirtyTracking(() =>
        {
            _editingProfileId = null;
            UserACombo.SelectedItem = null;
            UserBCombo.SelectedItem = null;
            HotkeyBox.Text = DefaultHotkey;
            ProfileNameBox.Text = string.Empty;
            EnabledCheck.IsChecked = true;
            ProfilesGrid.SelectedItem = null;
            AddOrUpdateButton.Content = "Add Profile";
        });

        _hasDraftChanges = false;
    }

    private void RefreshProfileGrid()
    {
        var snapshot = _profiles.ToList();
        _profiles.Clear();
        foreach (var profile in snapshot.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            _profiles.Add(profile);
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_hasUnsavedChanges && !_hasDraftChanges)
        {
            return;
        }

        var message = _hasUnsavedChanges && _hasDraftChanges
            ? "You have unsaved profile changes and unsaved form edits. Close without saving?"
            : _hasUnsavedChanges
                ? "You have unsaved profile changes. Close without saving?"
                : "You have unsaved form edits that were not added to a profile. Close without saving?";

        var decision = MessageBox.Show(
            this,
            message,
            "InstantLoginSwitcher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (decision != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void ProfileInputSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarkDraftChange();
    }

    private void ProfileInputTextChanged(object sender, TextChangedEventArgs e)
    {
        MarkDraftChange();
    }

    private void ProfileInputCheckedChanged(object sender, RoutedEventArgs e)
    {
        MarkDraftChange();
    }

    private void MarkDraftChange()
    {
        if (_dirtyTrackingSuspendDepth > 0)
        {
            return;
        }

        _hasDraftChanges = true;
        if (!_hasUnsavedChanges)
        {
            SetStatus("Form changed. Click Add Profile or Update Profile, then Save And Apply.");
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(InstallPaths.RootDirectory, isLogFile: false);
    }

    private void OpenListenerLog_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(InstallPaths.ListenerLogPath, isLogFile: true);
    }

    private void OpenSwitchLog_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(InstallPaths.SwitchLogPath, isLogFile: true);
    }

    private bool HasDuplicateProfile(string userA, string userB, string hotkey, Guid? excludedId)
    {
        var orderedUsers = new[] { userA, userB }.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var profile in _profiles)
        {
            if (excludedId.HasValue && profile.Id == excludedId.Value)
            {
                continue;
            }

            var profileOrderedUsers = new[] { profile.UserA, profile.UserB }.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            var sameUsers = string.Equals(orderedUsers[0], profileOrderedUsers[0], StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(orderedUsers[1], profileOrderedUsers[1], StringComparison.OrdinalIgnoreCase);

            if (sameUsers && profile.Hotkey.Equals(hotkey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void UpsertCredential(List<StoredUserCredential> destination, StoredUserCredential credential)
    {
        var existingIndex = destination.FindIndex(entry =>
            entry.UserName.Equals(credential.UserName, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            destination[existingIndex] = credential;
            return;
        }

        destination.Add(credential);
    }

    private void OpenPath(string path, bool isLogFile)
    {
        try
        {
            InstallPaths.EnsureRootDirectory();

            if (isLogFile && !File.Exists(path))
            {
                var folder = Path.GetDirectoryName(path) ?? InstallPaths.RootDirectory;
                MessageBox.Show(
                    this,
                    $"The log file does not exist yet.\n\nLocation: {path}\n\nThe data folder will open instead.",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Unable to open path:\n{path}\n\n{exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RunWithoutDirtyTracking(Action action)
    {
        _dirtyTrackingSuspendDepth++;
        try
        {
            action();
        }
        finally
        {
            _dirtyTrackingSuspendDepth--;
        }
    }

    private sealed class AccountOption
    {
        public AccountOption(LocalAdminAccount account)
        {
            Account = account;
            Label = string.Equals(account.FullName, account.UserName, StringComparison.OrdinalIgnoreCase)
                ? account.UserName
                : $"{account.UserName} ({account.FullName})";
        }

        public LocalAdminAccount Account { get; }
        public string Label { get; }
    }
}
