using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OrderTracker.Desktop.Converters;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (targetType == typeof(bool))
        {
            return value is not true;
        }

        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }

        return value is Visibility visibility && visibility != Visibility.Visible;
    }
}
