using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace BatteryHub.App;

public partial class DeviceFlyout : UserControl
{
    public DeviceFlyout()
    {
        InitializeComponent();
    }
}

/// <summary>Scales a 0-1 level to the level bar's width.</summary>
internal sealed class FractionToWidth(double fullWidth) : IValueConverter
{
    public static FractionToWidth Bar64 { get; } = new(64);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double fraction ? Math.Clamp(fraction, 0, 1) * fullWidth : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
