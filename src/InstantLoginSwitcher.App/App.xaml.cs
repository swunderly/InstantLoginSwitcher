using System.Windows;
using InstantLoginSwitcher.App.Services;
using InstantLoginSwitcher.Core.Services;

namespace InstantLoginSwitcher.App;

public partial class App : Application
{
    private ListenerRuntime? _listenerRuntime;

    private readonly ConfigService _configService = new();
    private readonly HotkeyParser _hotkeyParser = new();
    private readonly PasswordProtector _passwordProtector = new();
    private readonly CredentialValidator _credentialValidator = new();
    private readonly LocalAccountService _localAccountService = new();
    private readonly TaskSchedulerService _taskSchedulerService = new();

    private void OnStartup(object sender, StartupEventArgs eventArgs)
    {
        InstallPaths.EnsureRootDirectory();

        if (eventArgs.Args.Any(arg => arg.Equals("--listener", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            StartListenerMode();
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow(
            _configService,
            _hotkeyParser,
            _passwordProtector,
            _credentialValidator,
            _localAccountService,
            _taskSchedulerService,
            new SwitchExecutor(_passwordProtector));

        MainWindow = window;
        window.Show();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _listenerRuntime?.Dispose();
    }

    private void StartListenerMode()
    {
        try
        {
            _listenerRuntime = new ListenerRuntime(
                _configService,
                _hotkeyParser,
                new SwitchTargetResolver(),
                new SwitchExecutor(_passwordProtector));

            _listenerRuntime.Start();
        }
        catch (Exception exception)
        {
            FileLogger.WriteLine(InstallPaths.ListenerLogPath, "Listener startup failed: " + exception.Message);
            Shutdown(1);
        }
    }
}
