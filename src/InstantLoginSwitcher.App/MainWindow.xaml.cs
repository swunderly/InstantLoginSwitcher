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
    private const string AppTitle = "InstantLoginSwitcher";
    private const string DefaultHotkey = "Numpad4+Numpad6";
    private const int TaskStartConfirmationTimeoutMs = 1800;
    private const int TaskStartMutexTimeoutMs = 900;
    private const int DirectStartConfirmationTimeoutMs = 4200;
    private const int DirectStartMutexTimeoutMs = 1200;

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
        UpdateFileActionState();

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
                        Hotkey = TryCanonicalHotkey(profile.Hotkey),
                        Enabled = profile.Enabled
                    });
                }

                ClearFormInternal();
            });

            _hasUnsavedChanges = false;
            _hasDraftChanges = false;
            UpdateDirtyUiState();
            UpdateFormValidationState();
            UpdateSelectionActionState();
            UpdateFileActionState();
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
            UpdateDirtyUiState();
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
            UpdateDirtyUiState();
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
        UpdateDirtyUiState();
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
        if (!ResolveDraftBeforeSave())
        {
            return;
        }

        SwitcherConfig? configToSave = null;
        var configSaved = false;
        try
        {
            configToSave = BuildConfigFromUi();
            _configService.Save(configToSave);
            configSaved = true;

            var requiredUsers = GetRequiredUsersFromEnabledProfiles(configToSave.Profiles.Where(profile => profile.Enabled));

            var currentExecutable = GetCurrentExecutablePathOrThrow();

            _taskSchedulerService.SyncListenerTasks(requiredUsers, currentExecutable);
            var currentUserIncluded = requiredUsers.Any(user =>
                user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase));
            var currentUserListenerRunning = false;
            ListenerStartResult? listenerStartResult = null;
            if (requiredUsers.Count == 0)
            {
                _switchExecutor.DisableAutoLogon();
            }
            else if (currentUserIncluded)
            {
                listenerStartResult = EnsureListenerRunningForUser(
                    Environment.UserName,
                    currentExecutable,
                    tryTaskFirst: true);
                currentUserListenerRunning = listenerStartResult.RuntimeConfirmed;
            }

            _loadedConfig = configToSave;
            _hasUnsavedChanges = false;
            _hasDraftChanges = false;
            UpdateDirtyUiState();
            UpdateFileActionState();

            var saveStatus = requiredUsers.Count == 0
                ? "Configuration saved. No profiles enabled, startup tasks removed."
                : currentUserIncluded
                    ? currentUserListenerRunning
                        ? listenerStartResult is { TaskStartAttempted: true, DirectStartAttempted: true }
                            ? "Configuration saved, startup tasks updated, and listener is running (direct fallback used)."
                            : "Configuration saved, startup tasks updated, and listener is running."
                        : "Configuration saved and startup tasks updated, but listener runtime was not confirmed yet."
                    : "Configuration saved. Current user is not in any enabled profile, so no listener was started for this account.";
            SetStatus(saveStatus);

            var messageImage =
                currentUserIncluded && !currentUserListenerRunning
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information;
            var listenerErrorSuffix = string.IsNullOrWhiteSpace(listenerStartResult?.ErrorMessage)
                ? string.Empty
                : $"\n\nDetails: {listenerStartResult.ErrorMessage}";
            MessageBox.Show(
                this,
                requiredUsers.Count == 0
                    ? "Saved successfully. No active profiles remain, and startup tasks were removed."
                    : currentUserIncluded
                        ? currentUserListenerRunning
                            ? listenerStartResult is { TaskStartAttempted: true, DirectStartAttempted: true }
                                ? "Saved successfully. Hotkey profiles are active. Scheduled startup did not confirm immediately, so direct listener fallback was used."
                                : "Saved successfully. Hotkey profiles are now active for configured users."
                            : "Saved successfully, but listener runtime was not confirmed yet. Use Quick Fix Current User or Start Listener For Current User." + listenerErrorSuffix
                        : "Saved successfully. Startup tasks were updated, but this signed-in account is not part of an enabled profile.",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                messageImage);

            if (currentUserIncluded && !currentUserListenerRunning)
            {
                var openLog = MessageBox.Show(
                    this,
                    "Listener runtime was not confirmed yet.\n\nOpen listener.log now?" + listenerErrorSuffix,
                    "InstantLoginSwitcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (openLog == MessageBoxResult.Yes)
                {
                    OpenPath(InstallPaths.ListenerLogPath, isLogFile: true);
                }
            }
        }
        catch (Exception exception)
        {
            if (configSaved && configToSave is not null)
            {
                _loadedConfig = configToSave;
                _hasUnsavedChanges = false;
                _hasDraftChanges = false;
                UpdateDirtyUiState();
                UpdateFileActionState();
                SetStatus("Configuration saved, but startup task update failed. Use Quick Fix Current User or Repair + Check Setup.");
                MessageBox.Show(
                    this,
                    $"Configuration was saved, but startup task update failed.\n\n{exception.Message}\n\nUse Quick Fix Current User or Repair + Check Setup to retry.",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    this,
                    $"Save failed: {exception.Message}",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private bool ResolveDraftBeforeSave()
    {
        if (!_hasDraftChanges)
        {
            return true;
        }

        var (isDraftValid, validationMessage) = ValidateCurrentForm();
        if (!isDraftValid)
        {
            var ignoreDraft = MessageBox.Show(
                this,
                $"You have unsaved form edits that are not valid.\n\n{validationMessage}\n\nSave and apply without those form edits?",
                "InstantLoginSwitcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return ignoreDraft == MessageBoxResult.Yes;
        }

        var actionVerb = _editingProfileId.HasValue ? "update the selected profile" : "add a new profile";
        var decision = MessageBox.Show(
            this,
            $"You have unsaved form edits.\n\nYes: {actionVerb}, then Save And Apply.\nNo: save existing profiles only.\nCancel: go back.",
            "InstantLoginSwitcher",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (decision == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (decision == MessageBoxResult.No)
        {
            return true;
        }

        AddOrUpdateProfile_Click(this, new RoutedEventArgs());
        return !_hasDraftChanges;
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
        RunRepairStartupTasksWithDialogs();
    }

    private void RepairAndCheckSetup_Click(object sender, RoutedEventArgs e)
    {
        var hadUnsavedUiEdits = _hasUnsavedChanges || _hasDraftChanges;
        if (!RunRepairStartupTasksWithDialogs())
        {
            return;
        }

        if (hadUnsavedUiEdits)
        {
            SetStatus("Startup tasks repaired. Running setup check against saved config; click Save And Apply to align both.");
        }
        else
        {
            SetStatus("Startup tasks repaired. Running setup check...");
        }

        CheckSetup_Click(sender, e);
    }

    private void QuickFixCurrentUser_Click(object sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges || _hasDraftChanges)
        {
            MessageBox.Show(
                this,
                "Quick Fix uses saved config plus current startup/runtime state.\n\nClick Save And Apply first, then run Quick Fix.",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Run quick fix for current user?\n\nThis will repair startup tasks, start listener for current user, then run setup check.",
            "InstantLoginSwitcher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetStatus("Running quick fix for current user...");
        if (!RunRepairStartupTasksWithDialogs())
        {
            return;
        }

        if (!IsListenerMutexPresentForUser(Environment.UserName))
        {
            StartListenerForCurrentUser_Click(sender, new RoutedEventArgs());
        }

        CheckSetup_Click(sender, new RoutedEventArgs());
    }

    private void StartListenerForCurrentUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_hasUnsavedChanges || _hasDraftChanges)
            {
                var unsavedDecision = MessageBox.Show(
                    this,
                    "You have unsaved edits. Start Listener uses the saved config on disk.\n\nContinue anyway?",
                    "InstantLoginSwitcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (unsavedDecision != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var config = _configService.Load();
            var enabledProfiles = config.Profiles.Where(profile => profile.Enabled).ToList();
            if (enabledProfiles.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No enabled profiles are configured. Add or enable at least one profile, then click Save And Apply.",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var requiredUsers = GetRequiredUsersFromEnabledProfiles(enabledProfiles);
            var currentUser = Environment.UserName;
            var currentUserIncluded = requiredUsers.Any(user =>
                user.Equals(currentUser, StringComparison.OrdinalIgnoreCase));
            var listenerAlreadyRunning = IsListenerMutexPresentForUser(currentUser);
            if (!currentUserIncluded)
            {
                var decision = MessageBox.Show(
                    this,
                    $"Current user '{currentUser}' is not in any enabled saved profile.\n\nStart listener anyway?",
                    "InstantLoginSwitcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (decision != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            if (listenerAlreadyRunning)
            {
                SetStatus("Listener already appears to be running for current user.");
                MessageBox.Show(
                    this,
                    "Listener already appears to be running for the current user.\n\nTest hotkeys now. If they still do nothing, run Check Setup.",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var currentExecutable = GetCurrentExecutablePathOrThrow();
            var startResult = EnsureListenerRunningForUser(
                currentUser,
                currentExecutable,
                tryTaskFirst: currentUserIncluded);
            if (startResult.StartupConfirmed)
            {
                SetStatus(startResult is { TaskStartAttempted: true, DirectStartAttempted: true }
                    ? "Listener startup confirmed after direct fallback."
                    : "Listener startup confirmed. Test hotkeys now.");
                MessageBox.Show(
                    this,
                    startResult is { TaskStartAttempted: true, DirectStartAttempted: true }
                        ? "Listener startup confirmed after direct fallback.\n\nIf hotkeys still do nothing, run Check Setup or Save Diagnostics To File."
                        : "Listener startup confirmed.\n\nIf hotkeys still do nothing, run Check Setup or Save Diagnostics To File.",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (startResult.RuntimeConfirmed)
            {
                SetStatus("Listener appears to be running, but log confirmation was delayed.");
                MessageBox.Show(
                    this,
                    "Listener appears to be running, but startup log confirmation was delayed.\n\nTest hotkeys now.",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var errorSuffix = string.IsNullOrWhiteSpace(startResult.ErrorMessage)
                    ? string.Empty
                    : $"\n\nDetails: {startResult.ErrorMessage}";
                SetStatus("Listener start was attempted, but runtime confirmation was not detected yet.");
                var openLog = MessageBox.Show(
                    this,
                    "Listener start was attempted, but runtime confirmation was not detected yet.\n\nOpen listener.log now?" + errorSuffix,
                    "InstantLoginSwitcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (openLog == MessageBoxResult.Yes)
                {
                    OpenPath(InstallPaths.ListenerLogPath, isLogFile: true);
                }
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Could not start listener: {exception.Message}",
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
        if (_hasUnsavedChanges || _hasDraftChanges)
        {
            var decisionWithUnsaved = MessageBox.Show(
                this,
                "You have unsaved profile edits. Remove All Startup Tasks only affects tasks/auto-logon right now and does not save or delete profile edits. Continue?",
                "InstantLoginSwitcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decisionWithUnsaved != MessageBoxResult.Yes)
            {
                return;
            }
        }

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
        UpdateDirtyUiState();
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
        UpdateDirtyUiState();
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
        UpdateDirtyUiState();
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
            var chooserHint = BuildChooserHintForCandidate(
                firstUser.UserName,
                secondUser.UserName,
                parsed.CanonicalText,
                _editingProfileId,
                EnabledCheck.IsChecked ?? true);
            var message = $"Ready to {mode}. Hotkey will be saved as: {parsed.CanonicalText}";
            if (!string.IsNullOrWhiteSpace(chooserHint))
            {
                message += " " + chooserHint;
            }

            return (true, message);
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

    private void OpenConfigFile_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(InstallPaths.ConfigPath, isLogFile: true);
    }

    private void RestoreBackupConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InstallPaths.EnsureRootDirectory();
            if (!File.Exists(InstallPaths.ConfigBackupPath))
            {
                MessageBox.Show(
                    this,
                    $"Backup file not found:\n{InstallPaths.ConfigBackupPath}",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_hasUnsavedChanges || _hasDraftChanges)
            {
                var unsavedDecision = MessageBox.Show(
                    this,
                    "You have unsaved edits. Restoring backup will discard current unsaved changes. Continue?",
                    "InstantLoginSwitcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (unsavedDecision != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var confirmation = MessageBox.Show(
                this,
                "Replace the current config with backup and reload now?",
                "InstantLoginSwitcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            File.Copy(InstallPaths.ConfigBackupPath, InstallPaths.ConfigPath, overwrite: true);
            ReloadState();
            SetStatus("Configuration restored from backup.");
            UpdateFileActionState();
            MessageBox.Show(
                this,
                "Configuration restored from backup successfully.",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Restore backup failed: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenListenerLog_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(InstallPaths.ListenerLogPath, isLogFile: true);
    }

    private void OpenSwitchLog_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(InstallPaths.SwitchLogPath, isLogFile: true);
    }

    private void CheckSetup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = _configService.Load();
            var issues = new List<string>(GetConfigValidationIssues(config));
            var warnings = new List<string>();
            if (_hasUnsavedChanges || _hasDraftChanges)
            {
                warnings.Add("Unsaved UI edits are present. Setup check reads saved config until you click Save And Apply.");
            }

            if (config.Profiles.Count == 0)
            {
                warnings.Add("No profiles are configured.");
            }

            var enabledProfiles = config.Profiles.Where(profile => profile.Enabled).ToList();
            if (enabledProfiles.Count == 0)
            {
                warnings.Add("No profiles are enabled.");
            }

            var chooserRoutes = GetChooserRouteSummaries(enabledProfiles);
            if (chooserRoutes.Count > 0)
            {
                const int previewLimit = 4;
                var previewItems = chooserRoutes.Take(previewLimit).ToList();
                var remainder = chooserRoutes.Count - previewItems.Count;
                var suffix = remainder > 0 ? $" (+{remainder} more)" : string.Empty;
                warnings.Add("Chooser UI mode is active for: " + string.Join(", ", previewItems) + suffix + ".");
            }

            var requiredUsers = GetRequiredUsersFromEnabledProfiles(enabledProfiles);
            var currentUserIncluded = requiredUsers.Any(user =>
                user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase));
            if (requiredUsers.Count > 0 && !currentUserIncluded)
            {
                warnings.Add("Current signed-in user is not in any enabled profile.");
            }
            else if (currentUserIncluded && !IsListenerMutexPresentForUser(Environment.UserName))
            {
                warnings.Add("Current user's listener process does not appear to be running right now.");
            }

            try
            {
                var tasks = _taskSchedulerService.GetManagedTaskNamesForDiagnostics();
                var taskSet = tasks.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var expectedTaskNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var user in requiredUsers)
                {
                    expectedTaskNames[user] = _taskSchedulerService.GetTaskNameForUser(user);
                }

                var missingTaskUsers = expectedTaskNames
                    .Where(pair => !taskSet.Contains(pair.Value))
                    .Select(pair => pair.Key)
                    .OrderBy(user => user, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (missingTaskUsers.Count > 0)
                {
                    warnings.Add("Missing startup listener tasks for: " + string.Join(", ", missingTaskUsers) + ".");
                }

                var expectedTaskSet = expectedTaskNames.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var staleTasks = tasks
                    .Where(task => !expectedTaskSet.Contains(task))
                    .ToList();
                if (staleTasks.Count > 0)
                {
                    warnings.Add($"Found {staleTasks.Count} extra managed startup task(s) from older configs.");
                }

                if (currentUserIncluded)
                {
                    var expectedCurrentTask = _taskSchedulerService.GetTaskNameForUser(Environment.UserName);
                    if (!taskSet.Contains(expectedCurrentTask))
                    {
                        warnings.Add("Current user's startup listener task was not found.");
                    }
                }
            }
            catch (Exception exception)
            {
                warnings.Add("Could not verify startup tasks: " + exception.Message);
            }

            if (issues.Count == 0 && warnings.Count == 0)
            {
                SetStatus("Setup check passed.");
                MessageBox.Show(
                    this,
                    "Setup check passed. No configuration problems were found.",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Setup check found items to review.");
            builder.AppendLine($"Configuration issues: {issues.Count}");
            builder.AppendLine($"Warnings: {warnings.Count}");
            builder.AppendLine();

            if (issues.Count > 0)
            {
                builder.AppendLine("Configuration issues:");
            }
            foreach (var issue in issues)
            {
                builder.AppendLine("- " + issue);
            }

            if (warnings.Count > 0)
            {
                if (issues.Count > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("Warnings:");
            }
            foreach (var warning in warnings)
            {
                builder.AppendLine("- " + warning);
            }

            if (warnings.Any(warning => warning.Contains("startup listener task", StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine();
                builder.AppendLine("Tip: Click 'Quick Fix Current User' or 'Repair + Check Setup' to fix tasks and verify again.");
            }

            if (warnings.Any(warning => warning.Contains("Current user's startup listener task was not found.", StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("Tip: Click 'Quick Fix Current User' or 'Start Listener For Current User' to test hotkeys immediately.");
            }

            if (warnings.Any(warning => warning.Contains("listener process does not appear to be running", StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("Tip: Click 'Quick Fix Current User' and retest your hotkey.");
            }

            if (warnings.Any(warning => warning.Contains("Unsaved UI edits are present.", StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("Tip: Click Save And Apply, then run Check Setup again.");
            }

            SetStatus("Setup check found issues. Review the details dialog.");
            MessageBox.Show(
                this,
                builder.ToString(),
                "InstantLoginSwitcher Setup Check",
                MessageBoxButton.OK,
                issues.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Setup check failed: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        string diagnostics;
        try
        {
            diagnostics = BuildDiagnosticsSummary();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to build diagnostics: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
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
            try
            {
                var filePath = SaveDiagnosticsReportToFile(diagnostics);
                SetStatus("Clipboard copy failed. Diagnostics were saved to file.");
                MessageBox.Show(
                    this,
                    $"Clipboard copy failed: {exception.Message}\n\nDiagnostics were saved to:\n{filePath}",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception saveException)
            {
                MessageBox.Show(
                    this,
                    $"Clipboard copy failed: {exception.Message}\n\nAlso failed to save diagnostics to file: {saveException.Message}",
                    "InstantLoginSwitcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void SaveDiagnosticsToFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var diagnostics = BuildDiagnosticsSummary();
            var filePath = SaveDiagnosticsReportToFile(diagnostics);

            SetStatus($"Diagnostics saved to {filePath}");
            var openFolder = MessageBox.Show(
                this,
                $"Diagnostics saved:\n{filePath}\n\nOpen data folder now?",
                "InstantLoginSwitcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (openFolder == MessageBoxResult.Yes)
            {
                OpenPath(InstallPaths.RootDirectory, isLogFile: false);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to save diagnostics: {exception.Message}",
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

    private string BuildChooserHintForCandidate(
        string userA,
        string userB,
        string canonicalHotkey,
        Guid? excludedId,
        bool candidateEnabled)
    {
        if (!candidateEnabled)
        {
            return string.Empty;
        }

        var normalizedUserA = userA?.Trim() ?? string.Empty;
        var normalizedUserB = userB?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUserA) ||
            string.IsNullOrWhiteSpace(normalizedUserB) ||
            normalizedUserA.Equals(normalizedUserB, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var targetsByUser = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        static void AddTarget(
            Dictionary<string, HashSet<string>> map,
            string sourceUser,
            string targetUser)
        {
            if (!map.TryGetValue(sourceUser, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[sourceUser] = targets;
            }

            targets.Add(targetUser);
        }

        foreach (var profile in _profiles)
        {
            if (!profile.Enabled)
            {
                continue;
            }

            if (excludedId.HasValue && profile.Id == excludedId.Value)
            {
                continue;
            }

            var profileHotkey = TryCanonicalHotkey(profile.Hotkey);
            if (!profileHotkey.Equals(canonicalHotkey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var profileUserA = profile.UserA?.Trim() ?? string.Empty;
            var profileUserB = profile.UserB?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(profileUserA) ||
                string.IsNullOrWhiteSpace(profileUserB) ||
                profileUserA.Equals(profileUserB, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddTarget(targetsByUser, profileUserA, profileUserB);
            AddTarget(targetsByUser, profileUserB, profileUserA);
        }

        AddTarget(targetsByUser, normalizedUserA, normalizedUserB);
        AddTarget(targetsByUser, normalizedUserB, normalizedUserA);

        var impactedUsers = new List<string>();
        foreach (var user in new[] { normalizedUserA, normalizedUserB }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!targetsByUser.TryGetValue(user, out var targets))
            {
                continue;
            }

            if (targets.Count > 1)
            {
                impactedUsers.Add($"{user} ({targets.Count} targets)");
            }
        }

        if (impactedUsers.Count == 0)
        {
            return string.Empty;
        }

        return "Chooser UI will appear for " + string.Join(", ", impactedUsers) + " on this hotkey.";
    }

    private bool RunRepairStartupTasksWithDialogs()
    {
        try
        {
            return RunRepairStartupTasksCore(promptWhenUnsavedEdits: true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Task repair failed: {exception.Message}",
                "InstantLoginSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool RunRepairStartupTasksCore(bool promptWhenUnsavedEdits)
    {
        if (promptWhenUnsavedEdits && (_hasUnsavedChanges || _hasDraftChanges))
        {
            var decision = MessageBox.Show(
                this,
                "You have unsaved profile edits. Repair will use the current profiles shown in this window, not necessarily what is already saved to disk. Continue?",
                "InstantLoginSwitcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decision != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        var requiredUsers = _profiles
            .Where(profile => profile.Enabled)
            .SelectMany(profile => new[] { profile.UserA, profile.UserB })
            .Select(user => user?.Trim() ?? string.Empty)
            .Where(user => !string.IsNullOrWhiteSpace(user))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentExecutable = GetCurrentExecutablePathOrThrow();

        _taskSchedulerService.SyncListenerTasks(requiredUsers, currentExecutable);
        var currentUserIncluded = requiredUsers.Any(user =>
            user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        if (requiredUsers.Count == 0)
        {
            _switchExecutor.DisableAutoLogon();
            SetStatus("No active profiles found. Startup tasks removed and auto-logon values cleared.");
            return true;
        }

        if (currentUserIncluded)
        {
            var listenerStartResult = EnsureListenerRunningForUser(
                Environment.UserName,
                currentExecutable,
                tryTaskFirst: true);
            var repairFailureHint = string.IsNullOrWhiteSpace(listenerStartResult.ErrorMessage)
                ? string.Empty
                : $" Details: {listenerStartResult.ErrorMessage}";
            SetStatus(listenerStartResult.RuntimeConfirmed
                ? listenerStartResult is { TaskStartAttempted: true, DirectStartAttempted: true }
                    ? "Startup tasks repaired and listener is running (direct fallback used)."
                    : "Startup tasks repaired for configured profiles."
                : "Startup tasks repaired, but listener runtime was not confirmed yet. Use Quick Fix Current User." + repairFailureHint);
            return true;
        }

        SetStatus("Startup tasks repaired. Current user is not in an enabled profile, so listener was not started for this account.");
        return true;
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
                var missingFileName = Path.GetFileName(path);
                MessageBox.Show(
                    this,
                    $"The file does not exist yet ({missingFileName}).\n\nLocation: {path}\n\nThe data folder will open instead.",
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

    private static string SaveDiagnosticsReportToFile(string diagnosticsText)
    {
        InstallPaths.EnsureRootDirectory();
        var fileName = $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt";
        var filePath = Path.Combine(InstallPaths.RootDirectory, fileName);
        if (File.Exists(filePath))
        {
            filePath = Path.Combine(
                InstallPaths.RootDirectory,
                $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt");
        }

        InstallPaths.WriteUtf8NoBom(filePath, diagnosticsText + Environment.NewLine);
        return filePath;
    }

    private static string GetCurrentExecutablePathOrThrow()
    {
        var currentExecutable = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExecutable))
        {
            throw new InvalidOperationException("Could not resolve application executable path.");
        }

        return currentExecutable;
    }

    private ListenerStartResult EnsureListenerRunningForUser(string userName, string executablePath, bool tryTaskFirst)
    {
        var result = new ListenerStartResult();
        if (string.IsNullOrWhiteSpace(userName))
        {
            result.ErrorMessage = "User name is required to start listener.";
            return result;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            result.ErrorMessage = "Executable path is required to start listener.";
            return result;
        }

        if (IsListenerMutexPresentForUser(userName))
        {
            result.RuntimeConfirmed = true;
            return result;
        }

        if (tryTaskFirst)
        {
            result.TaskStartAttempted = true;
            try
            {
                var taskBaseline = CaptureListenerLogBaseline(InstallPaths.ListenerLogPath);
                _taskSchedulerService.StartListenerForUser(userName);
                var taskStartupConfirmed = WaitForListenerStartupConfirmation(taskBaseline, timeoutMs: TaskStartConfirmationTimeoutMs);
                result.StartupConfirmed = taskStartupConfirmed;
                result.RuntimeConfirmed = taskStartupConfirmed || WaitForListenerMutex(userName, timeoutMs: TaskStartMutexTimeoutMs);
                if (result.RuntimeConfirmed)
                {
                    return result;
                }
            }
            catch (Exception exception)
            {
                result.ErrorMessage = "Scheduled start failed: " + exception.Message;
            }
        }

        result.DirectStartAttempted = true;
        try
        {
            var directBaseline = CaptureListenerLogBaseline(InstallPaths.ListenerLogPath);
            StartListenerProcess(executablePath);
            var directStartupConfirmed = WaitForListenerStartupConfirmation(directBaseline, timeoutMs: DirectStartConfirmationTimeoutMs);
            result.StartupConfirmed = result.StartupConfirmed || directStartupConfirmed;
            result.RuntimeConfirmed = directStartupConfirmed || WaitForListenerMutex(userName, timeoutMs: DirectStartMutexTimeoutMs);
        }
        catch (Exception exception)
        {
            var directStartError = "Direct start failed: " + exception.Message;
            result.ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? directStartError
                : result.ErrorMessage + " | " + directStartError;
        }

        return result;
    }

    private static void StartListenerProcess(string executablePath)
    {
        var started = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = "--listener",
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (started is null)
        {
            throw new InvalidOperationException("Listener process did not start.");
        }
    }

    private static ListenerLogBaseline CaptureListenerLogBaseline(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new ListenerLogBaseline
                {
                    LastWriteUtc = DateTime.MinValue,
                    FileSizeBytes = -1
                };
            }

            var info = new FileInfo(path);
            return new ListenerLogBaseline
            {
                LastWriteUtc = info.LastWriteTimeUtc,
                FileSizeBytes = info.Length
            };
        }
        catch
        {
            return new ListenerLogBaseline
            {
                LastWriteUtc = DateTime.MinValue,
                FileSizeBytes = -1
            };
        }
    }

    private static bool WaitForListenerStartupConfirmation(ListenerLogBaseline baseline, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 500));
        while (DateTime.UtcNow < deadline)
        {
            if (HasListenerStartupMarkerSince(baseline))
            {
                return true;
            }

            Thread.Sleep(200);
        }

        return HasListenerStartupMarkerSince(baseline);
    }

    private static bool HasListenerStartupMarkerSince(ListenerLogBaseline baseline)
    {
        try
        {
            if (!File.Exists(InstallPaths.ListenerLogPath))
            {
                return false;
            }

            var info = new FileInfo(InstallPaths.ListenerLogPath);
            var fileChangedSinceBaseline =
                baseline.FileSizeBytes < 0 ||
                info.LastWriteTimeUtc > baseline.LastWriteUtc ||
                info.Length != baseline.FileSizeBytes;
            if (!fileChangedSinceBaseline)
            {
                return false;
            }

            using var stream = new FileStream(
                InstallPaths.ListenerLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var startOffset = 0L;
            if (baseline.FileSizeBytes >= 0 && baseline.FileSizeBytes <= stream.Length)
            {
                startOffset = baseline.FileSizeBytes;
            }

            stream.Seek(startOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var appendedText = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(appendedText) && startOffset > 0)
            {
                return false;
            }

            return appendedText.Contains("Listener started. combos=", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsListenerMutexPresentForUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var mutexName = GetListenerMutexName(userName.Trim());
        try
        {
            if (!Mutex.TryOpenExisting(mutexName, out var existingMutex))
            {
                return false;
            }

            existingMutex.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForListenerMutex(string userName, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 500));
        while (DateTime.UtcNow < deadline)
        {
            if (IsListenerMutexPresentForUser(userName))
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return IsListenerMutexPresentForUser(userName);
    }

    private static string GetListenerMutexName(string userName)
    {
        return $@"Local\InstantLoginSwitcher.Listener.{userName}";
    }

    private sealed class ListenerStartResult
    {
        public bool TaskStartAttempted { get; set; }
        public bool DirectStartAttempted { get; set; }
        public bool StartupConfirmed { get; set; }
        public bool RuntimeConfirmed { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    private readonly struct ListenerLogBaseline
    {
        public DateTime LastWriteUtc { get; init; }
        public long FileSizeBytes { get; init; }
    }

    private string BuildDiagnosticsSummary()
    {
        InstallPaths.EnsureRootDirectory();

        var diagnosticsErrors = new List<string>();
        SwitcherConfig config;
        try
        {
            config = _configService.Load();
        }
        catch (Exception exception)
        {
            diagnosticsErrors.Add("Config load failed: " + exception.Message);
            config = new SwitcherConfig();
        }

        var enabledProfiles = config.Profiles.Where(profile => profile.Enabled).ToList();
        var requiredUsers = GetRequiredUsersFromEnabledProfiles(enabledProfiles);
        var chooserRoutes = GetChooserRouteSummaries(enabledProfiles);

        List<string> startupTasks;
        try
        {
            startupTasks = _taskSchedulerService
                .GetManagedTaskNamesForDiagnostics()
                .OrderBy(task => task, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception)
        {
            diagnosticsErrors.Add("Startup task query failed: " + exception.Message);
            startupTasks = new List<string>();
        }

        string expectedCurrentUserTask;
        try
        {
            expectedCurrentUserTask = _taskSchedulerService.GetTaskNameForUser(Environment.UserName);
        }
        catch (Exception exception)
        {
            diagnosticsErrors.Add("Expected current-user task name failed: " + exception.Message);
            expectedCurrentUserTask = "(unavailable: " + exception.Message + ")";
        }

        var startupTaskSet = startupTasks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasCurrentUserTask = startupTaskSet.Contains(expectedCurrentUserTask);
        var expectedTaskByUser = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingExpectedUsers = new List<string>();
        foreach (var user in requiredUsers)
        {
            try
            {
                var expectedTask = _taskSchedulerService.GetTaskNameForUser(user);
                expectedTaskByUser[user] = expectedTask;
                if (!startupTaskSet.Contains(expectedTask))
                {
                    missingExpectedUsers.Add(user);
                }
            }
            catch (Exception exception)
            {
                diagnosticsErrors.Add($"Expected task name failed for '{user}': {exception.Message}");
                missingExpectedUsers.Add(user);
            }
        }

        var expectedTaskNameSet = expectedTaskByUser.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpectedStartupTasks = startupTasks
            .Where(task => !expectedTaskNameSet.Contains(task))
            .OrderBy(task => task, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("InstantLoginSwitcher Diagnostics");
        builder.AppendLine($"UTC: {DateTime.UtcNow:o}");
        builder.AppendLine($"Machine: {Environment.MachineName}");
        builder.AppendLine($"CurrentUser: {Environment.UserName}");
        builder.AppendLine($"UiUnsavedProfileChanges: {_hasUnsavedChanges}");
        builder.AppendLine($"UiUnsavedDraftChanges: {_hasDraftChanges}");
        builder.AppendLine($"CurrentUserListenerMutexPresent: {IsListenerMutexPresentForUser(Environment.UserName)}");
        builder.AppendLine($"{InstallPaths.RootOverrideEnvironmentVariable}: {Environment.GetEnvironmentVariable(InstallPaths.RootOverrideEnvironmentVariable) ?? "(not set)"}");
        builder.AppendLine($"DataFolder: {InstallPaths.RootDirectory}");
        builder.AppendLine($"ConfigPath: {InstallPaths.ConfigPath}");
        builder.AppendLine($"ConfigExists: {File.Exists(InstallPaths.ConfigPath)}");
        builder.AppendLine($"ConfigLastWriteUtc: {GetFileLastWriteUtcText(InstallPaths.ConfigPath)}");
        builder.AppendLine($"ConfigBackupPath: {InstallPaths.ConfigBackupPath}");
        builder.AppendLine($"ConfigBackupExists: {File.Exists(InstallPaths.ConfigBackupPath)}");
        builder.AppendLine($"ConfigBackupLastWriteUtc: {GetFileLastWriteUtcText(InstallPaths.ConfigBackupPath)}");
        builder.AppendLine($"PendingAutoLogonMarker: {File.Exists(InstallPaths.PendingAutoLogonMarkerPath)}");
        builder.AppendLine($"ConfigProfilesTotal: {config.Profiles.Count}");
        builder.AppendLine($"ConfigProfilesEnabled: {enabledProfiles.Count}");
        builder.AppendLine($"ChooserRouteCount: {chooserRoutes.Count}");
        builder.AppendLine($"ConfigUsersWithCredentials: {config.Users.Count(user => !string.IsNullOrWhiteSpace(user.PasswordEncrypted))}");
        builder.AppendLine($"CurrentUserInEnabledProfiles: {requiredUsers.Any(user => user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))}");
        builder.AppendLine($"ExpectedCurrentUserTask: {expectedCurrentUserTask}");
        builder.AppendLine($"ExpectedCurrentUserTaskPresent: {hasCurrentUserTask}");
        builder.AppendLine($"MissingExpectedTasks: {missingExpectedUsers.Count}");
        builder.AppendLine($"UnexpectedManagedTasks: {unexpectedStartupTasks.Count}");

        IReadOnlyList<string> validationIssues;
        try
        {
            validationIssues = GetConfigValidationIssues(config);
        }
        catch (Exception exception)
        {
            diagnosticsErrors.Add("Validation scan failed: " + exception.Message);
            validationIssues = Array.Empty<string>();
        }

        builder.AppendLine($"ValidationIssues: {validationIssues.Count}");
        builder.AppendLine($"DiagnosticsErrors: {diagnosticsErrors.Count}");

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

        builder.AppendLine("ChooserRoutes:");
        if (chooserRoutes.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var route in chooserRoutes)
            {
                builder.AppendLine($"  - {route}");
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

        builder.AppendLine("ExpectedTasksByUser:");
        if (expectedTaskByUser.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var pair in expectedTaskByUser.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var state = startupTaskSet.Contains(pair.Value) ? "present" : "missing";
                builder.AppendLine($"  - {pair.Key}: {pair.Value} [{state}]");
            }
        }

        builder.AppendLine("UnexpectedStartupTasks:");
        if (unexpectedStartupTasks.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var task in unexpectedStartupTasks)
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

        builder.AppendLine("DiagnosticsErrorDetails:");
        if (diagnosticsErrors.Count == 0)
        {
            builder.AppendLine("  (none)");
        }
        else
        {
            foreach (var issue in diagnosticsErrors)
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

    private static List<string> GetRequiredUsersFromEnabledProfiles(IEnumerable<SwitchProfile> enabledProfiles)
    {
        return enabledProfiles
            .SelectMany(profile => new[] { profile.UserA, profile.UserB })
            .Select(user => user?.Trim() ?? string.Empty)
            .Where(user => !string.IsNullOrWhiteSpace(user))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(user => user, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> GetChooserRouteSummaries(IEnumerable<SwitchProfile> enabledProfiles)
    {
        var targetsByRoute = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        static string BuildRouteKey(string sourceUser, string canonicalHotkey)
        {
            return sourceUser + "|" + canonicalHotkey;
        }

        static void AddTarget(
            Dictionary<string, HashSet<string>> map,
            string sourceUser,
            string canonicalHotkey,
            string targetUser)
        {
            var key = BuildRouteKey(sourceUser, canonicalHotkey);
            if (!map.TryGetValue(key, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = targets;
            }

            targets.Add(targetUser);
        }

        foreach (var profile in enabledProfiles.Where(profile => profile.Enabled))
        {
            var userA = profile.UserA?.Trim() ?? string.Empty;
            var userB = profile.UserB?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userA) ||
                string.IsNullOrWhiteSpace(userB) ||
                userA.Equals(userB, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string canonicalHotkey;
            try
            {
                canonicalHotkey = _hotkeyParser.Parse(profile.Hotkey).CanonicalText;
            }
            catch
            {
                continue;
            }

            AddTarget(targetsByRoute, userA, canonicalHotkey, userB);
            AddTarget(targetsByRoute, userB, canonicalHotkey, userA);
        }

        var result = new List<string>();
        foreach (var pair in targetsByRoute.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Value.Count <= 1)
            {
                continue;
            }

            var separatorIndex = pair.Key.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex >= pair.Key.Length - 1)
            {
                continue;
            }

            var user = pair.Key[..separatorIndex];
            var hotkey = pair.Key[(separatorIndex + 1)..];
            result.Add($"{user} on {hotkey} ({pair.Value.Count} targets)");
        }

        return result;
    }

    private IReadOnlyList<string> GetConfigValidationIssues(SwitcherConfig config)
    {
        var issues = new List<string>();
        var credentialByUser = config.Users
            .GroupBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var duplicateGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in config.Profiles.Where(profile => profile.Enabled))
        {
            var profileName = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id.ToString() : profile.Name;
            var userA = profile.UserA?.Trim() ?? string.Empty;
            var userB = profile.UserB?.Trim() ?? string.Empty;
            string? canonicalHotkey = null;
            try
            {
                canonicalHotkey = _hotkeyParser.Parse(profile.Hotkey).CanonicalText;
            }
            catch (Exception exception)
            {
                issues.Add($"Profile '{profileName}' has invalid hotkey '{profile.Hotkey}': {exception.Message}");
            }

            if (!string.IsNullOrWhiteSpace(userA) &&
                !string.IsNullOrWhiteSpace(userB) &&
                userA.Equals(userB, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Profile '{profileName}' has identical users.");
            }

            foreach (var userName in new[] { userA, userB })
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

            if (!string.IsNullOrWhiteSpace(canonicalHotkey) &&
                !string.IsNullOrWhiteSpace(userA) &&
                !string.IsNullOrWhiteSpace(userB))
            {
                var orderedUsers = new[] { userA, userB }
                    .OrderBy(user => user, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var signature = $"{orderedUsers[0]}|{orderedUsers[1]}|{canonicalHotkey}";
                if (!duplicateGuard.Add(signature))
                {
                    issues.Add($"Duplicate enabled profile mapping detected for users '{orderedUsers[0]}' and '{orderedUsers[1]}' on hotkey '{canonicalHotkey}'.");
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

    private static string GetFileLastWriteUtcText(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "(missing)";
            }

            return File.GetLastWriteTimeUtc(path).ToString("o");
        }
        catch (Exception exception)
        {
            return "(unavailable: " + exception.Message + ")";
        }
    }

    private void UpdateSelectionActionState()
    {
        var hasSelection = ProfilesGrid.SelectedItem is ProfileEditorModel;
        RemoveSelectedButton.IsEnabled = hasSelection;
        UpdatePasswordsButton.IsEnabled = hasSelection;
        RemoveSelectedButton.ToolTip = hasSelection ? "Remove the selected profile." : "Select a profile row first.";
        UpdatePasswordsButton.ToolTip = hasSelection
            ? "Re-enter passwords for both users in the selected profile."
            : "Select a profile row first.";
    }

    private void UpdateFileActionState()
    {
        try
        {
            InstallPaths.EnsureRootDirectory();
            var configExists = File.Exists(InstallPaths.ConfigPath);
            var backupExists = File.Exists(InstallPaths.ConfigBackupPath);

            OpenConfigButton.ToolTip = configExists
                ? "Open the current config file."
                : "Config file does not exist yet. Click to open the data folder instead.";

            RestoreBackupButton.IsEnabled = backupExists;
            RestoreBackupButton.ToolTip = backupExists
                ? "Replace current config with the backup file and reload."
                : "No backup config is available yet.";
        }
        catch (Exception exception)
        {
            OpenConfigButton.ToolTip = "File status unavailable: " + exception.Message;
            RestoreBackupButton.IsEnabled = false;
            RestoreBackupButton.ToolTip = "File status unavailable: " + exception.Message;
        }
    }

    private void UpdateDirtyUiState()
    {
        Title = (_hasUnsavedChanges || _hasDraftChanges) ? $"{AppTitle} *" : AppTitle;
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
