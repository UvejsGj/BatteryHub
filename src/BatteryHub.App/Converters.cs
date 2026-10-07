using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BatteryHub.App;

/// <summary>Collapsed when the value is null or false (or, with Invert, when it is non-null or true).</summary>
internal sealed class VisibleWhenConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is bool flag ? flag : value is not null;
        return visible ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
