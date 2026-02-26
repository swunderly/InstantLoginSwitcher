using System.Collections.ObjectModel;
using System.Windows;
using InstantLoginSwitcher.App.Models;
using InstantLoginSwitcher.App.Views;
using InstantLoginSwitcher.Core.Models;
using InstantLoginSwitcher.Core.Services;

namespace InstantLoginSwitcher.App.Services;

public sealed class ListenerRuntime : IDisposable
{
    private readonly ConfigService _configService;
    private readonly HotkeyParser _hotkeyParser;
    private readonly SwitchTargetResolver _targetResolver;
    private readonly SwitchExecutor _switchExecutor;
    private readonly LowLevelKeyboardHook _keyboardHook;

    private readonly object _stateLock = new();
    private readonly HashSet<int> _pressedKeys = new();

    private FileSystemWatcher? _configWatcher;
    private List<ListenerHotkeyBinding> _bindings = new();
    private SwitcherConfig _config = new();
    private int _switchInProgress;
    private readonly object _reloadDebounceLock = new();
    private CancellationTokenSource? _reloadDebounceSource;

    public ListenerRuntime(
        ConfigService configService,
        HotkeyParser hotkeyParser,
        SwitchTargetResolver targetResolver,
        SwitchExecutor switchExecutor)
    {
        _configService = configService;
        _hotkeyParser = hotkeyParser;
        _targetResolver = targetResolver;
        _switchExecutor = switchExecutor;

        _keyboardHook = new LowLevelKeyboardHook();
        _keyboardHook.KeyStateChanged += OnKeyStateChanged;
    }

    public void Start()
    {
        ReloadConfig();
        StartConfigWatcher();

        _keyboardHook.Start();
        FileLogger.WriteLine(InstallPaths.ListenerLogPath, $"Listener started. combos={_bindings.Count}");
    }

    public void Dispose()
    {
        lock (_reloadDebounceLock)
        {
            _reloadDebounceSource?.Cancel();
            _reloadDebounceSource?.Dispose();
            _reloadDebounceSource = null;
        }

        if (_configWatcher is not null)
        {
            _configWatcher.Changed -= OnConfigFileChanged;
            _configWatcher.Created -= OnConfigFileChanged;
            _configWatcher.Renamed -= OnConfigFileChanged;
            _configWatcher.Dispose();
            _configWatcher = null;
        }

        _keyboardHook.Dispose();
        GC.SuppressFinalize(this);
    }

    private void StartConfigWatcher()
    {
        var directory = Path.GetDirectoryName(InstallPaths.ConfigPath);
        var fileName = Path.GetFileName(InstallPaths.ConfigPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        _configWatcher = new FileSystemWatcher(directory, fileName)
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName
        };

        _configWatcher.Changed += OnConfigFileChanged;
        _configWatcher.Created += OnConfigFileChanged;
        _configWatcher.Renamed += OnConfigFileChanged;
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs eventArgs)
    {
        CancellationTokenSource nextTokenSource;
        lock (_reloadDebounceLock)
        {
            _reloadDebounceSource?.Cancel();
            _reloadDebounceSource?.Dispose();
            _reloadDebounceSource = new CancellationTokenSource();
            nextTokenSource = _reloadDebounceSource;
        }

        _ = DebouncedReloadAsync(nextTokenSource.Token);
    }

    private async Task DebouncedReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                ReloadConfig();
                FileLogger.WriteLine(InstallPaths.ListenerLogPath, "Configuration reloaded.");
            }
            catch (Exception exception)
            {
                FileLogger.WriteLine(InstallPaths.ListenerLogPath, "Config reload failed: " + exception.Message);
            }
        });
    }

    private void ReloadConfig()
    {
        var loaded = _configService.Load();
        var nextBindings = new List<ListenerHotkeyBinding>();
        var seenCanonicals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hotkey in loaded.Profiles
                     .Where(profile => profile.Enabled)
                     .Select(profile => profile.Hotkey))
        {
            try
            {
                var definition = _hotkeyParser.Parse(hotkey);
                if (!seenCanonicals.Add(definition.CanonicalText))
                {
                    FileLogger.WriteLine(InstallPaths.ListenerLogPath, $"Duplicate combo ignored: {definition.CanonicalText}");
                    continue;
                }

                nextBindings.Add(new ListenerHotkeyBinding
                {
                    CanonicalHotkey = definition.CanonicalText,
                    Definition = definition,
                    IsTriggered = false
                });

                FileLogger.WriteLine(InstallPaths.ListenerLogPath, $"Loaded combo {definition.CanonicalText}");
            }
            catch (Exception exception)
            {
                FileLogger.WriteLine(InstallPaths.ListenerLogPath, $"Invalid configured hotkey '{hotkey}': {exception.Message}");
            }
        }

        lock (_stateLock)
        {
            _config = loaded;
            _bindings = nextBindings;
            _pressedKeys.Clear();
        }
    }

    private void OnKeyStateChanged(int virtualKey, bool isPressed)
    {
        var triggerQueue = new List<string>();
        lock (_stateLock)
        {
            if (isPressed)
            {
                _pressedKeys.Add(virtualKey);
            }
            else
            {
                _pressedKeys.Remove(virtualKey);
            }

            foreach (var binding in _bindings)
            {
                var pressedNow = binding.Definition.IsPressed(_pressedKeys);
                if (pressedNow && !binding.IsTriggered)
                {
                    binding.IsTriggered = true;
                    triggerQueue.Add(binding.CanonicalHotkey);
                }
                else if (!pressedNow)
                {
                    binding.IsTriggered = false;
                }
            }
        }

        foreach (var hotkey in triggerQueue)
        {
            _ = Task.Run(() => TriggerSwitchAsync(hotkey));
        }
    }

    private async Task TriggerSwitchAsync(string hotkeyCanonical)
    {
        if (Interlocked.Exchange(ref _switchInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            var currentUser = Environment.UserName;
            SwitcherConfig config;
            lock (_stateLock)
            {
                config = _config;
            }

            var targets = _targetResolver.ResolveTargets(config, hotkeyCanonical, currentUser);
            if (targets.Count == 0)
            {
                FileLogger.WriteLine(InstallPaths.ListenerLogPath, $"No switch target for user '{currentUser}' and hotkey '{hotkeyCanonical}'.");
                return;
            }

            SwitchTarget? selectedTarget;
            if (targets.Count == 1)
            {
                selectedTarget = targets[0];
            }
            else
            {
                selectedTarget = await ShowChooserAsync(targets);
                if (selectedTarget is null)
                {
                    FileLogger.WriteLine(InstallPaths.ListenerLogPath, "Switch cancelled in chooser UI.");
                    return;
                }
            }

            FileLogger.WriteLine(InstallPaths.ListenerLogPath, $"Triggering switch to '{selectedTarget.UserName}' on '{hotkeyCanonical}'.");
            _switchExecutor.ExecuteSwitch(config, hotkeyCanonical, currentUser, selectedTarget.UserName);
        }
        catch (Exception exception)
        {
            FileLogger.WriteLine(InstallPaths.ListenerLogPath, "Switch trigger failed: " + exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _switchInProgress, 0);
        }
    }

    private static Task<SwitchTarget?> ShowChooserAsync(IReadOnlyList<SwitchTarget> targets)
    {
        var completion = new TaskCompletionSource<SwitchTarget?>();
        Application.Current.Dispatcher.Invoke(() =>
        {
            var chooser = new ChooserWindow(new ObservableCollection<SwitchTarget>(targets));
            var result = chooser.ShowDialog();
            completion.SetResult(result == true ? chooser.SelectedTarget : null);
        });

        return completion.Task;
    }
}
