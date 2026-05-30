using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OrderTracker.Desktop.Converters;

public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isOpen = value is bool boolValue && boolValue;
        if (!isOpen)
        {
            return new GridLength(0);
        }

        var widthText = parameter?.ToString();
        return double.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && width > 0
            ? new GridLength(width)
            : GridLength.Auto;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
