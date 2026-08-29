using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OrderTracker.Desktop.Converters;

public sealed class ResponsiveItemWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double availableWidth || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return DependencyProperty.UnsetValue;
        }

        var mode = parameter?.ToString();
        if (string.Equals(mode, "OrderSearch", StringComparison.OrdinalIgnoreCase))
        {
            var reservedWidth = availableWidth >= 1100d ? 650d : 34d;
            return Math.Max(180d, availableWidth - reservedWidth);
        }

        var isChart = string.Equals(mode, "Chart", StringComparison.OrdinalIgnoreCase);
        var columns = isChart ? GetChartColumns(availableWidth) : GetMetricColumns(availableWidth);
        var width = isChart
            ? GetExternalMarginWidth(availableWidth, columns, 14d)
            : availableWidth / columns;

        return Math.Max(0d, width);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static int GetMetricColumns(double width)
    {
        if (width >= 1180)
        {
            return 4;
        }

        if (width >= 860)
        {
            return 3;
        }

        return width >= 560 ? 2 : 1;
    }

    private static int GetChartColumns(double width)
    {
        return width >= 980 ? 2 : 1;
    }

    private static double GetExternalMarginWidth(double availableWidth, int columns, double itemRightMargin)
    {
        return Math.Floor((availableWidth - itemRightMargin * columns) / columns);
    }
}
