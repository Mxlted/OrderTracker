using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OrderTracker.Desktop.Converters;

public sealed class AdaptiveEditorReserveWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double availableWidth || values[1] is not bool isOpen || !isOpen)
        {
            return targetType == typeof(Visibility) ? Visibility.Collapsed : new GridLength(0);
        }

        var parts = (parameter?.ToString() ?? string.Empty).Split(',');
        var editorWidth = parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth)
            ? parsedWidth
            : 400d;
        var minimumGridWidth = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMinimumGridWidth)
            ? parsedMinimumGridWidth
            : 900d;

        var hasReservedWidth = double.IsFinite(availableWidth) && availableWidth - editorWidth >= minimumGridWidth;
        if (targetType == typeof(Visibility))
        {
            return hasReservedWidth ? Visibility.Collapsed : Visibility.Visible;
        }

        return hasReservedWidth ? new GridLength(editorWidth) : new GridLength(0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
