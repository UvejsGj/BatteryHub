using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace BatteryHub.App;

/// <summary>Flyout colours that follow the Windows taskbar's light or dark mode.</summary>
internal static class Theme
{
    public static void Apply(ResourceDictionary resources)
    {
        bool light = SystemUsesLightTheme();
        Set(resources, "FlyoutBackground", light ? "#F9F9F9" : "#2C2C2C");
        Set(resources, "FlyoutBorder", light ? "#D0D0D0" : "#454545");
        Set(resources, "FlyoutForeground", light ? "#1A1A1A" : "#F2F2F2");
        Set(resources, "FlyoutSecondary", light ? "#5F5F5F" : "#B4B4B4");
        Set(resources, "FlyoutLevelBar", light ? "#2EA043" : "#3FB950");
        Set(resources, "FlyoutLow", light ? "#C42B1C" : "#FF99A4");
        Set(resources, "FlyoutTrack", light ? "#E0E0E0" : "#3D3D3D");
    }

    // The flyout sits on the taskbar, so follow the system (taskbar) setting rather than the apps one.
    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    private static void Set(ResourceDictionary resources, string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        resources[key] = brush;
    }
}
