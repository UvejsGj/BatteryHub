using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using BatteryHub.Core;
using BatteryHub.Core.Presentation;
using BatteryHub.Core.Settings;
using BatteryHub.Core.Sony;
using BatteryHub.Core.Windows;
using Microsoft.Extensions.Logging;

namespace BatteryHub.App;

/// <summary>What the tray icon, its flyout and its menu bind to. Used on the UI thread only.</summary>
internal sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly BatteryMonitor _monitor;
    private readonly SettingsStore _store;
    private readonly SonyHidProvider _sony;
    private readonly StartupRegistration _startup;
    private readonly ILogger _logger;
    private AppSettings _settings;
    private long _shownVersion = -1;
    private bool _refreshing;
    private DateTime? _lastUpdated;

    public MainViewModel(BatteryMonitor monitor, SettingsStore store, AppSettings settings, SonyHidProvider sony, StartupRegistration startup, ILogger logger)
    {
        _monitor = monitor;
        _store = store;
        _settings = settings;
        _sony = sony;
        _startup = startup;
        _logger = logger;
        RefreshCommand = new RelayCommand(Refresh, () => !_refreshing);
        ToggleStartWithWindowsCommand = new RelayCommand(ToggleStartWithWindows);
        ToggleReadBluetoothCommand = new RelayCommand(ToggleReadBluetooth);
        ExitCommand = new RelayCommand(() => Application.Current.Shutdown());
        RefreshMenuState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<DeviceRow> Rows { get; private set; } = [];

    public bool HasDevices => Rows.Count > 0;

    public string ToolTip { get; private set; } = "BatteryHub: checking devices…";

    public string StatusLine => _refreshing || _lastUpdated is null
        ? "Checking devices…"
        : $"Updated {_lastUpdated.Value.ToString("t", CultureInfo.CurrentCulture)}";

    public bool StartWithWindows { get; private set; }

    public bool ReadBluetoothPlayStationControllers => _settings.ReadBluetoothPlayStationControllers;

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ToggleStartWithWindowsCommand { get; }

    public RelayCommand ToggleReadBluetoothCommand { get; }

    public RelayCommand ExitCommand { get; }

    /// <summary>Shows a snapshot unless a newer one is already on screen (snapshots can arrive out of order).</summary>
    public void Apply(BatterySnapshot snapshot)
    {
        if (snapshot.Version <= _shownVersion)
        {
            return;
        }

        _shownVersion = snapshot.Version;
        Rows = DeviceListPresenter.Rows(snapshot.Readings);
        ToolTip = DeviceListPresenter.ToolTip(snapshot.Readings);
        _lastUpdated = DateTime.Now;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(StatusLine));
    }

    /// <summary>Re-reads state that can change outside the app (Task Manager can disable the startup entry).</summary>
    public void RefreshMenuState()
    {
        try
        {
            StartWithWindows = _startup.IsEnabled;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger.LogWarning(ex, "Could not read the startup entry");
            StartWithWindows = false;
        }

        OnPropertyChanged(nameof(StartWithWindows));
    }

    private async void Refresh()
    {
        SetRefreshing(true);
        try
        {
            await _monitor.RefreshAsync();
        }
        catch (ObjectDisposedException)
        {
            // Exiting.
        }
        finally
        {
            SetRefreshing(false);
        }
    }

    private void SetRefreshing(bool refreshing)
    {
        _refreshing = refreshing;
        OnPropertyChanged(nameof(StatusLine));
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void ToggleStartWithWindows()
    {
        try
        {
            if (_startup.IsEnabled)
            {
                _startup.Disable();
            }
            else
            {
                _startup.Enable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger.LogWarning(ex, "Could not change the startup entry");
        }

        RefreshMenuState();
    }

    private void ToggleReadBluetooth()
    {
        _settings = _settings with { ReadBluetoothPlayStationControllers = !_settings.ReadBluetoothPlayStationControllers };
        _sony.Options = _sony.Options with { RequestFullBluetoothReports = _settings.ReadBluetoothPlayStationControllers };
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Could not save settings to {Path}", _store.FilePath);
        }

        OnPropertyChanged(nameof(ReadBluetoothPlayStationControllers));
        Refresh();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
