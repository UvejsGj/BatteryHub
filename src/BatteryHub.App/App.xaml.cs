using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using BatteryHub.Core;
using BatteryHub.Core.Settings;
using BatteryHub.Core.Sony;
using BatteryHub.Core.Windows;
using H.NotifyIcon;

namespace BatteryHub.App;

public partial class App : Application
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IconRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly Uri IconUri = new("pack://application:,,,/Assets/BatteryHub.ico");

    private readonly TraceLogger<App> _logger = new();
    private SingleInstance? _instance;
    private BatteryMonitor? _monitor;
    private TaskbarIcon? _tray;
    private string? _shownToolTip;
    private DeviceFlyout? _flyout;
    private DispatcherTimer? _iconRetry;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = SingleInstance.TryClaim();
        if (_instance is null)
        {
            SingleInstance.ShowRunningInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, default, "Unobserved task exception", args.Exception, (s, _) => s);

        var store = new SettingsStore(SettingsStore.DefaultDirectory, new TraceLogger<SettingsStore>());
        var settings = store.Load();
        var sony = new SonyHidProvider(
            new SonyHidOptions { RequestFullBluetoothReports = settings.ReadBluetoothPlayStationControllers },
            new TraceLogger<SonyHidProvider>());
        _monitor = new BatteryMonitor([sony], new TraceLogger<BatteryMonitor>());
        var startup = new StartupRegistration(new StartupRegistry(), Environment.ProcessPath ?? throw new InvalidOperationException("No process path."));
        var viewModel = new MainViewModel(_monitor, store, settings, sony, startup, _logger);

        _tray = (TaskbarIcon)FindResource("TrayIcon");
        _flyout = (DeviceFlyout)_tray.TrayPopup!;
        Theme.Apply(_flyout.Resources);
        _tray.Icon = LoadTrayIcon();
        _tray.DataContext = viewModel;
        _tray.PreviewTrayPopupOpen += (_, _) => Theme.Apply(_flyout.Resources);
        _tray.TrayContextMenuOpen += (_, _) => viewModel.RefreshMenuState();
        _tray.TrayKeyboardKeySelect += (_, _) => ShowFlyout();
        _tray.TrayKeyboardContextMenu += (_, _) => ShowMenuFromKeyboard();

        // After an Explorer restart the library re-adds the icon itself but swallows a failure; this handler runs
        // after the library's (both on the UI thread) and falls back to the retry timer if the icon is still missing.
        var tray = _tray;
        tray.TrayIcon.MessageWindow.TaskbarCreated += (_, _) =>
        {
            _shownToolTip = null;
            if (!tray.IsCreated)
            {
                CreateTrayIcon(tray);
            }
        };
        CreateTrayIcon(tray);

        _monitor.Updated += (_, args) => Dispatcher.BeginInvoke(() =>
        {
            viewModel.Apply(args.Snapshot);
            SetToolTip(viewModel.ToolTip);
        });
        _instance.ShowRequested += () => Dispatcher.BeginInvoke(ShowFlyout);
        _monitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _iconRetry?.Stop();
            _tray?.Dispose();
            if (_monitor is not null && !_monitor.DisposeAsync().AsTask().Wait(ShutdownTimeout))
            {
                // Providers may be mid-read; exit regardless.
                LogWarning("Providers did not stop in time", null);
            }
        }
        catch (Exception ex)
        {
            LogWarning("Cleanup on exit failed", ex);
        }
        finally
        {
            _instance?.Dispose();
            base.OnExit(e);
        }
    }

    // The library loads an icon source at the small-icon size divided by the display scale, so above 100% it picks
    // the 16 px frame and the shell stretches it. Load the frame the shell will actually draw.
    private static Icon LoadTrayIcon()
    {
        using var stream = GetResourceStream(IconUri)!.Stream;
        return new Icon(stream, NativeMethods.GetSystemMetrics(NativeMethods.SmCxSmIcon), NativeMethods.GetSystemMetrics(NativeMethods.SmCySmIcon));
    }

    // At sign-in the app can start before the taskbar is ready, and a busy shell can refuse the icon. The library
    // re-adds it when Explorer announces a new taskbar (TaskbarCreated); a shell that merely timed out sends no such
    // message, so also retry until the icon is in.
    private void CreateTrayIcon(TaskbarIcon tray)
    {
        try
        {
            tray.ForceCreate(enablesEfficiencyMode: false);
            _iconRetry?.Stop();
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("Could not add the tray icon yet; retrying", ex);
            if (_iconRetry is null)
            {
                _iconRetry = new DispatcherTimer { Interval = IconRetryInterval };
                _iconRetry.Tick += (_, _) =>
                {
                    if (tray.IsCreated)
                    {
                        _iconRetry.Stop();
                    }
                    else
                    {
                        CreateTrayIcon(tray);
                    }
                };
            }

            _iconRetry.Start();
        }
    }

    // Goes through TrayIcon directly: re-setting the ToolTipText property to an unchanged value would not reach the
    // shell, so a failed update could never be retried. The shell refuses updates while Explorer restarts; the next
    // snapshot retries, and storing the text means a re-added icon (TaskbarCreated) shows it too.
    private void SetToolTip(string text)
    {
        if (_tray is null || _shownToolTip == text)
        {
            return;
        }

        try
        {
            _tray.TrayIcon.UpdateToolTip(text);
            _shownToolTip = text;
        }
        catch (InvalidOperationException ex)
        {
            _tray.TrayIcon.ToolTip = text;
            _shownToolTip = null; // what the shell shows is now unknown, so the next snapshot re-sends
            LogWarning("Could not update the tray tooltip", ex);
        }
    }

    // Finding the tray's position fails while Explorer is down; there is nothing to show the flyout next to then.
    private void ShowFlyout()
    {
        try
        {
            _tray?.ShowTrayPopup(TaskbarIcon.GetPopupTrayPosition());
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("Could not open the flyout", ex);
        }
    }

    // Windows sends the menu key's message after a right-click too, when the mouse handler has already opened it.
    private void ShowMenuFromKeyboard()
    {
        if (_tray?.ContextMenu is not { IsOpen: false })
        {
            return;
        }

        try
        {
            _tray.ShowContextMenu(TaskbarIcon.GetPopupTrayPosition());
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("Could not open the menu", ex);
        }
    }

    private void LogWarning(string message, Exception? ex) =>
        _logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, default, message, ex, (s, _) => s);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Log(Microsoft.Extensions.Logging.LogLevel.Critical, default, "Unhandled exception", e.Exception, (s, _) => s);
        MessageBox.Show($"BatteryHub hit an unexpected error and will close.\n\n{e.Exception.Message}", "BatteryHub", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
