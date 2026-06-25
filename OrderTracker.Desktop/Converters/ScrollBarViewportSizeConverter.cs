using System;
using System.Globalization;
using System.Windows.Data;

namespace OrderTracker.Desktop.Converters;

public sealed class ScrollBarViewportSizeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 ||
            !TryGetDouble(values[0], out var minimum) ||
            !TryGetDouble(values[1], out var maximum) ||
            !TryGetDouble(values[2], out var viewportSize))
        {
            return 0d;
        }

        var range = maximum - minimum;
        if (range <= 0 || viewportSize <= 0)
        {
            return viewportSize;
        }

        var minimumThumbRatio = ParseRatio(parameter, 0.34);
        var naturalThumbRatio = viewportSize / (range + viewportSize);
        if (naturalThumbRatio >= minimumThumbRatio)
        {
            return viewportSize;
        }

        return range * minimumThumbRatio / (1 - minimumThumbRatio);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool TryGetDouble(object value, out double result)
    {
        result = 0;
        if (value is not double doubleValue ||
            double.IsNaN(doubleValue) ||
            double.IsInfinity(doubleValue))
        {
            return false;
        }

        result = doubleValue;
        return true;
    }

    private static double ParseRatio(object? parameter, double fallback)
    {
        if (parameter is not null &&
            double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
        {
            return Math.Clamp(ratio, 0.1, 0.8);
        }

        return fallback;
    }
}
