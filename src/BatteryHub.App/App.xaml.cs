using System.Windows;

namespace BatteryHub.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Placeholder until the tray shell (milestone 3) exists.
        MessageBox.Show(
            "The BatteryHub tray app is not built yet. Use BatteryHub.Probe to list devices.",
            "BatteryHub",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Shutdown();
    }
}
