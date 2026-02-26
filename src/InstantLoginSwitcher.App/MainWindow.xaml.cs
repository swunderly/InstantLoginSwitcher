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
    private readonly ConfigService _configService;
    private readonly HotkeyParser _hotkeyParser;
    private readonly PasswordProtector _passwordProtector;
    private readonly CredentialValidator _credentialValidator;
    private readonly LocalAccountService _localAccountService;
    private readonly TaskSchedulerService _taskSchedulerService;
    private readonly SwitchExecutor _switchExecutor;

    private readonly ObservableCollection<ProfileEditorModel> _profiles = new();
    private List<AccountOption> _accountOptions = new();
    private SwitcherConfig _loadedConfig = new();
    private Guid? _editingProfileId;

    public MainWindow(
        ConfigService configService,
        HotkeyParser hotkeyParser,
        PasswordProtector passwordProtector,
        CredentialValidator credentialValidator,
        LocalAccountService localAccountService,
        TaskSchedulerService taskSchedulerService,
        SwitchExecutor switchExecutor)
    {
        _configService = configService;
        _hotkeyParser = hotkeyParser;
        _passwordProtector = passwordProtector;
        _credentialValidator = credentialValidator;
        _localAccountService = localAccountService;
        _taskSchedulerService = taskSchedulerService;
        _switchExecutor = switchExecutor;

        InitializeComponent();
        ProfilesGrid.ItemsSource = _profiles;

        ReloadState();
    }

    private void ReloadState()
    {
        try
        {
            _loadedConfig = _configService.Load();
            _accountOptions = _localAccountService
                .GetEnabledLocalAdministrators()
                .Select(account => new AccountOption(account))
                .ToList();

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
            SetStatus("Profile updated.");
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

            SetStatus("Profile added.");
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

        _profiles.Remove(selected);
        ClearFormInternal();
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
            _taskSchedulerService.StartListenerForUser(Environment.UserName);

            _loadedConfig = configToSave;
            SetStatus("Configuration saved and startup tasks updated.");

            MessageBox.Show(
                this,
                "Saved successfully. Hotkey profiles are now active for configured users.",
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

        _editingProfileId = selected.Id;
        ProfileNameBox.Text = selected.Name;
        HotkeyBox.Text = selected.Hotkey;
        EnabledCheck.IsChecked = selected.Enabled;
        UserACombo.SelectedItem = _accountOptions.FirstOrDefault(option =>
            option.Account.UserName.Equals(selected.UserA, StringComparison.OrdinalIgnoreCase));
        UserBCombo.SelectedItem = _accountOptions.FirstOrDefault(option =>
            option.Account.UserName.Equals(selected.UserB, StringComparison.OrdinalIgnoreCase));
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
            PicturePath = string.Empty
        };
    }

    private LocalAdminAccount? GetSelectedAccount(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as AccountOption)?.Account;
    }

    private void ClearFormInternal()
    {
        _editingProfileId = null;
        UserACombo.SelectedItem = null;
        UserBCombo.SelectedItem = null;
        HotkeyBox.Text = string.Empty;
        ProfileNameBox.Text = string.Empty;
        EnabledCheck.IsChecked = true;
        ProfilesGrid.SelectedItem = null;
        AddOrUpdateButton.Content = "Add Profile";
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
