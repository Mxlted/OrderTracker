using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Converters;

public sealed class ChartAccentBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var accent = values.Length > 0 ? values[0]?.ToString()?.ToUpperInvariant() : null;
        var theme = values.Length > 1 && values[1] is AppTheme appTheme
            ? appTheme
            : AppTheme.Dark;

        var color = theme switch
        {
            AppTheme.Light => accent switch
            {
                "#5CC8FF" => "#0078B8",
                "#2F9E7E" => "#168765",
                "#FFB547" or "#F5A524" => "#A85A00",
                "#7C9BFF" => "#4B63C6",
                "#E05D5D" => "#C93D4B",
                "#B389FF" => "#7250B5",
                "#F57FB0" => "#C93D77",
                _ => "#516174"
            },
            AppTheme.OLED => accent switch
            {
                "#5CC8FF" => "#00B7F0",
                "#2F9E7E" => "#33D69F",
                "#FFB547" or "#F5A524" => "#FFB547",
                "#7C9BFF" => "#8DA6FF",
                "#E05D5D" => "#FF6B6B",
                "#B389FF" => "#C29AFF",
                "#F57FB0" => "#FF7FC2",
                _ => "#8793A3"
            },
            _ => accent ?? "#6B7A90"
        };

        return BrushCache.Get(color);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
