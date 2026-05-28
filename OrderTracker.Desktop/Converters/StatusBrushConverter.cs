using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Converters;

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            OrderStatus.Delivered => "#2F9E7E",
            OrderStatus.OutForDelivery => "#5CC8FF",
            OrderStatus.Shipped => "#7C9BFF",
            OrderStatus.Delayed => "#F5A524",
            OrderStatus.Cancelled or OrderStatus.Returned => "#E05D5D",
            OrderStatus.Processing => "#B389FF",
            _ => "#6B7A90"
        };

        return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
