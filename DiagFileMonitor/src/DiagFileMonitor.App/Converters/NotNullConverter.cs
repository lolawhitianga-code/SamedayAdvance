using System.Globalization;
using System.Windows.Data;

namespace DiagFileMonitor.App.Converters;

/// <summary>True when the bound value is set, for enabling controls that act on a selected row.</summary>
public class NotNullConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
