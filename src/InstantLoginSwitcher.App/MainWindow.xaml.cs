using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        ProfilesGrid.ItemsSource = _profiles;
        UpdateSelectionActionState();
        UpdateFormValidationState();

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
            UpdateFormValidationState();
            UpdateSelectionActionState();
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
        if (_hasUnsavedChanges || _hasDraftChanges)
        {
            MessageBox.Show(
                this,
                "You have unsaved profile edits. Click Save And Apply first, then update passwords.",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

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
        if (_hasUnsavedChanges || _hasDraftChanges)
        {
            var decision = MessageBox.Show(
                this,
                "You have unsaved changes. Reloading will discard them. Continue?",
                "InstantLoginSwitcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decision != MessageBoxResult.Yes)
            {
                return;
            }
        }

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
            _editingProfileId = null;
            AddOrUpdateButton.Content = "Add Profile";
            UpdateFormValidationState();
            UpdateSelectionActionState();
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
        UpdateFormValidationState();
        UpdateSelectionActionState();
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
            var normalizedUserA = profile.UserA.Trim();
            var normalizedUserB = profile.UserB.Trim();
            var normalizedName = string.IsNullOrWhiteSpace(profile.Name)
                ? $"{normalizedUserA} <-> {normalizedUserB}"
                : profile.Name.Trim();
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
                Name = normalizedName,
                UserA = normalizedUserA,
                UserB = normalizedUserB,
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
        UpdateFormValidationState();
        UpdateSelectionActionState();
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

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveAndApply_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None &&
            e.Key == Key.Enter &&
            IsProfileInputFocused() &&
            AddOrUpdateButton.IsEnabled)
        {
            AddOrUpdateProfile_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None &&
            e.Key == Key.Escape &&
            IsProfileInputFocused())
        {
            ClearFormInternal();
            SetStatus("Profile form cleared.");
            e.Handled = true;
        }
    }

    private bool IsProfileInputFocused()
    {
        if (UserACombo.IsDropDownOpen || UserBCombo.IsDropDownOpen)
        {
            return false;
        }

        return UserACombo.IsKeyboardFocusWithin ||
               UserBCombo.IsKeyboardFocusWithin ||
               HotkeyBox.IsKeyboardFocusWithin ||
               ProfileNameBox.IsKeyboardFocusWithin ||
               EnabledCheck.IsKeyboardFocusWithin;
    }

    private void ProfileInputSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TryAutoFillOppositeUser(sender);
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

    private void TryAutoFillOppositeUser(object sender)
    {
        if (_dirtyTrackingSuspendDepth > 0 || _accountOptions.Count != 2)
        {
            return;
        }

        if (sender == UserACombo &&
            UserACombo.SelectedItem is AccountOption first &&
            UserBCombo.SelectedItem is null)
        {
            var candidate = _accountOptions.FirstOrDefault(option =>
                !option.Account.UserName.Equals(first.Account.UserName, StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                return;
            }

            RunWithoutDirtyTracking(() =>
            {
                UserBCombo.SelectedItem = candidate;
            });
            return;
        }

        if (sender == UserBCombo &&
            UserBCombo.SelectedItem is AccountOption second &&
            UserACombo.SelectedItem is null)
        {
            var candidate = _accountOptions.FirstOrDefault(option =>
                !option.Account.UserName.Equals(second.Account.UserName, StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                return;
            }

            RunWithoutDirtyTracking(() =>
            {
                UserACombo.SelectedItem = candidate;
            });
        }
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var raw = HotkeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            var parsed = _hotkeyParser.Parse(raw);
            if (string.Equals(parsed.CanonicalText, raw, StringComparison.Ordinal))
            {
                return;
            }

            RunWithoutDirtyTracking(() =>
            {
                HotkeyBox.Text = parsed.CanonicalText;
            });
            MarkDraftChange();
            UpdateFormValidationState();
        }
        catch
        {
            // Validation message is already shown by live form validation.
        }
    }

    private void MarkDraftChange()
    {
        if (_dirtyTrackingSuspendDepth > 0)
        {
            return;
        }

        _hasDraftChanges = true;
        UpdateFormValidationState();
        if (!_hasUnsavedChanges)
        {
            SetStatus("Form changed. Click Add Profile or Update Profile, then Save And Apply.");
        }
    }

    private void UpdateFormValidationState()
    {
        var (isValid, message) = ValidateCurrentForm();
        AddOrUpdateButton.IsEnabled = isValid;
        AddOrUpdateButton.ToolTip = message;
        FormHintText.Text = message;
        FormHintText.Foreground = isValid
            ? new SolidColorBrush(Color.FromRgb(0x1E, 0x6B, 0x24))
            : new SolidColorBrush(Color.FromRgb(0x8A, 0x5A, 0x00));
    }

    private (bool IsValid, string Message) ValidateCurrentForm()
    {
        if (_accountOptions.Count < 2)
        {
            return (false, "At least two enabled local administrator accounts are required.");
        }

        if (GetSelectedAccount(UserACombo) is null)
        {
            return (false, "Choose a first user to build a profile.");
        }

        if (GetSelectedAccount(UserBCombo) is null)
        {
            return (false, "Choose a second user to build a profile.");
        }

        var firstUser = GetSelectedAccount(UserACombo)!;
        var secondUser = GetSelectedAccount(UserBCombo)!;
        if (firstUser.UserName.Equals(secondUser.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "First and second user must be different.");
        }

        if (string.IsNullOrWhiteSpace(HotkeyBox.Text))
        {
            return (false, "Enter a hotkey (example: Ctrl+Alt+S).");
        }

        try
        {
            var parsed = _hotkeyParser.Parse(HotkeyBox.Text);
            if (HasDuplicateProfile(firstUser.UserName, secondUser.UserName, parsed.CanonicalText, _editingProfileId))
            {
                return (false, "A profile with the same users and hotkey already exists.");
            }

            var mode = _editingProfileId.HasValue ? "update" : "add";
            return (true, $"Ready to {mode}. Hotkey will be saved as: {parsed.CanonicalText}");
        }
        catch (Exception exception)
        {
            return (false, $"Hotkey issue: {exception.Message}");
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

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var diagnostics = BuildDiagnosticsSummary();
            Clipboard.SetText(diagnostics);
            SetStatus("Diagnostics copied to clipboard.");
            MessageBox.Show(
                this,
                "Diagnostics were copied to your clipboard. You can paste them into GitHub issues or support messages.",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to copy diagnostics: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool HasDuplicateProfile(string userA, string userB, string hotkey, Guid? excludedId)
    {
        var candidateHotkeyCanonical = TryCanonicalHotkey(hotkey);
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
            var profileHotkeyCanonical = TryCanonicalHotkey(profile.Hotkey);

            if (sameUsers && profileHotkeyCanonical.Equals(candidateHotkeyCanonical, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string TryCanonicalHotkey(string hotkeyText)
    {
        try
        {
            return _hotkeyParser.Parse(hotkeyText).CanonicalText;
        }
        catch
        {
            return hotkeyText?.Trim() ?? string.Empty;
        }
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

    private string BuildDiagnosticsSummary()
    {
        InstallPaths.EnsureRootDirectory();

        var config = _configService.Load();
        var enabledProfiles = config.Profiles.Where(profile => profile.Enabled).ToList();
        var requiredUsers = enabledProfiles
            .SelectMany(profile => new[] { profile.UserA, profile.UserB })
            .Where(user => !string.IsNullOrWhiteSpace(user))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(user => user, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var startupTasks = _taskSchedulerService
            .GetManagedTaskNamesForDiagnostics()
            .OrderBy(task => task, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expectedCurrentUserTask = _taskSchedulerService.GetTaskNameForUser(Environment.UserName);
        var hasCurrentUserTask = startupTasks.Any(task =>
            task.Equals(expectedCurrentUserTask, StringComparison.OrdinalIgnoreCase));

        var builder = new StringBuilder();
        builder.AppendLine("InstantLoginSwitcher Diagnostics");
        builder.AppendLine($"UTC: {DateTime.UtcNow:o}");
        builder.AppendLine($"Machine: {Environment.MachineName}");
        builder.AppendLine($"CurrentUser: {Environment.UserName}");
        builder.AppendLine($"DataFolder: {InstallPaths.RootDirectory}");
        builder.AppendLine($"ConfigPath: {InstallPaths.ConfigPath}");
        builder.AppendLine($"ConfigBackupPath: {InstallPaths.ConfigBackupPath}");
        builder.AppendLine($"ConfigBackupExists: {File.Exists(InstallPaths.ConfigBackupPath)}");
        builder.AppendLine($"PendingAutoLogonMarker: {File.Exists(InstallPaths.PendingAutoLogonMarkerPath)}");
        builder.AppendLine($"ConfigProfilesTotal: {config.Profiles.Count}");
        builder.AppendLine($"ConfigProfilesEnabled: {enabledProfiles.Count}");
        builder.AppendLine($"ConfigUsersWithCredentials: {config.Users.Count(user => !string.IsNullOrWhiteSpace(user.PasswordEncrypted))}");
        builder.AppendLine($"CurrentUserInEnabledProfiles: {requiredUsers.Any(user => user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))}");
        builder.AppendLine($"ExpectedCurrentUserTask: {expectedCurrentUserTask}");
        builder.AppendLine($"ExpectedCurrentUserTaskPresent: {hasCurrentUserTask}");

        var validationIssues = GetConfigValidationIssues(config);
        builder.AppendLine($"ValidationIssues: {validationIssues.Count}");

        builder.AppendLine("EnabledProfiles:");
        if (enabledProfiles.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var profile in enabledProfiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"  - {profile.Name} | {profile.UserA}<->{profile.UserB} | {profile.Hotkey}");
            }
        }

        builder.AppendLine("StartupTasks:");
        if (startupTasks.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var task in startupTasks)
            {
                builder.AppendLine($"  - {task}");
            }
        }

        builder.AppendLine("ValidationIssueDetails:");
        if (validationIssues.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var issue in validationIssues)
            {
                builder.AppendLine("  - " + issue);
            }
        }

        builder.AppendLine("ListenerLogTail:");
        AppendLogTail(builder, InstallPaths.ListenerLogPath);
        builder.AppendLine("SwitchLogTail:");
        AppendLogTail(builder, InstallPaths.SwitchLogPath);

        return builder.ToString();
    }

    private IReadOnlyList<string> GetConfigValidationIssues(SwitcherConfig config)
    {
        var issues = new List<string>();
        var credentialByUser = config.Users
            .GroupBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var profile in config.Profiles.Where(profile => profile.Enabled))
        {
            var profileName = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id.ToString() : profile.Name;
            try
            {
                _hotkeyParser.Parse(profile.Hotkey);
            }
            catch (Exception exception)
            {
                issues.Add($"Profile '{profileName}' has invalid hotkey '{profile.Hotkey}': {exception.Message}");
            }

            foreach (var userName in new[] { profile.UserA, profile.UserB })
            {
                if (string.IsNullOrWhiteSpace(userName))
                {
                    issues.Add($"Profile '{profileName}' has a blank user value.");
                    continue;
                }

                if (!credentialByUser.TryGetValue(userName, out var credential) ||
                    string.IsNullOrWhiteSpace(credential.PasswordEncrypted))
                {
                    issues.Add($"Profile '{profileName}' is missing a saved password for '{userName}'.");
                }
            }
        }

        return issues;
    }

    private static void AppendLogTail(StringBuilder builder, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                builder.AppendLine("  (missing)");
                return;
            }

            const int tailSize = 60;
            var tail = new Queue<string>(tailSize);
            foreach (var line in File.ReadLines(path))
            {
                if (tail.Count == tailSize)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            foreach (var line in tail)
            {
                builder.AppendLine("  " + line);
            }
        }
        catch (Exception exception)
        {
            builder.AppendLine($"  (read failed: {exception.Message})");
        }
    }

    private void UpdateSelectionActionState()
    {
        var hasSelection = ProfilesGrid.SelectedItem is ProfileEditorModel;
        RemoveSelectedButton.IsEnabled = hasSelection;
        UpdatePasswordsButton.IsEnabled = hasSelection;
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
