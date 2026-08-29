using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Converters;

public sealed class StatusBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var status = values.Length > 0 && values[0] is OrderStatus orderStatus
            ? orderStatus
            : OrderStatus.Ordered;
        var theme = values.Length > 1 && values[1] is AppTheme appTheme
            ? appTheme
            : AppTheme.Dark;
        var foreground = string.Equals(parameter?.ToString(), "Foreground", StringComparison.OrdinalIgnoreCase);
        var color = (theme, status, foreground) switch
        {
            (AppTheme.Light, OrderStatus.Delivered, false) => "#D7F3EA",
            (AppTheme.Light, OrderStatus.Delivered, true) => "#075E45",
            (AppTheme.Light, OrderStatus.OutForDelivery, false) => "#D9F2FF",
            (AppTheme.Light, OrderStatus.OutForDelivery, true) => "#004D73",
            (AppTheme.Light, OrderStatus.Shipped, false) => "#E2E8FF",
            (AppTheme.Light, OrderStatus.Shipped, true) => "#28408A",
            (AppTheme.Light, OrderStatus.Delayed, false) => "#FFF0C7",
            (AppTheme.Light, OrderStatus.Delayed, true) => "#714400",
            (AppTheme.Light, OrderStatus.Cancelled or OrderStatus.Returned, false) => "#FCE0E0",
            (AppTheme.Light, OrderStatus.Cancelled or OrderStatus.Returned, true) => "#8A2430",
            (AppTheme.Light, OrderStatus.Processing, false) => "#EEE2FF",
            (AppTheme.Light, OrderStatus.Processing, true) => "#54308A",
            (AppTheme.Light, _, false) => "#E2E8F0",
            (AppTheme.Light, _, true) => "#334155",

            (AppTheme.OLED, OrderStatus.Delivered, false) => "#0B4D3A",
            (AppTheme.OLED, OrderStatus.Delivered, true) => "#CFFFF0",
            (AppTheme.OLED, OrderStatus.OutForDelivery, false) => "#074D69",
            (AppTheme.OLED, OrderStatus.OutForDelivery, true) => "#D9F5FF",
            (AppTheme.OLED, OrderStatus.Shipped, false) => "#263E7A",
            (AppTheme.OLED, OrderStatus.Shipped, true) => "#EDF1FF",
            (AppTheme.OLED, OrderStatus.Delayed, false) => "#664208",
            (AppTheme.OLED, OrderStatus.Delayed, true) => "#FFF0BE",
            (AppTheme.OLED, OrderStatus.Cancelled or OrderStatus.Returned, false) => "#68262E",
            (AppTheme.OLED, OrderStatus.Cancelled or OrderStatus.Returned, true) => "#FFE7EA",
            (AppTheme.OLED, OrderStatus.Processing, false) => "#4B2F70",
            (AppTheme.OLED, OrderStatus.Processing, true) => "#F2E7FF",
            (AppTheme.OLED, _, false) => "#354150",
            (AppTheme.OLED, _, true) => "#F2F5F8",

            (_, OrderStatus.Delivered, false) => "#174F40",
            (_, OrderStatus.Delivered, true) => "#D7FFF2",
            (_, OrderStatus.OutForDelivery, false) => "#174E68",
            (_, OrderStatus.OutForDelivery, true) => "#D9F4FF",
            (_, OrderStatus.Shipped, false) => "#34447A",
            (_, OrderStatus.Shipped, true) => "#EEF1FF",
            (_, OrderStatus.Delayed, false) => "#6A4712",
            (_, OrderStatus.Delayed, true) => "#FFF1C7",
            (_, OrderStatus.Cancelled or OrderStatus.Returned, false) => "#672D33",
            (_, OrderStatus.Cancelled or OrderStatus.Returned, true) => "#FFE8EA",
            (_, OrderStatus.Processing, false) => "#4A3768",
            (_, OrderStatus.Processing, true) => "#F3E9FF",
            (_, _, false) => "#394454",
            (_, _, true) => "#F2F5F8"
        };

        return BrushCache.Get(color);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
