using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiagFileMonitor.App.Converters;

/// <summary>Visible when the bound value is false, for showing a fallback in place of something.</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
