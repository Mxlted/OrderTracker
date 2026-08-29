using System.Globalization;
using System.Windows.Data;

namespace OrderTracker.Desktop.Converters;

public sealed class WidthBelowThresholdConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width))
        {
            return false;
        }

        var threshold = parameter?.ToString() is { } text &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedThreshold)
                ? parsedThreshold
                : 900d;

        return width > 0 && width < threshold;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
