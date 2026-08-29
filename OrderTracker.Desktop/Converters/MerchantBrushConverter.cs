using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Converters;

public sealed class MerchantBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            MerchantKind.Amazon => "#FFB547",
            MerchantKind.Walmart => "#3CA4FF",
            MerchantKind.Target => "#E65D5D",
            MerchantKind.BestBuy => "#F7D154",
            MerchantKind.eBay => "#7CDB7C",
            MerchantKind.Other => "#A7B0C0",
            _ => "#5B6678"
        };

        return BrushCache.Get(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
