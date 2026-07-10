using System.Globalization;
using System.Windows.Data;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Converters;

public sealed partial class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            OrderSortOption.NewestFirst => "Newest order date",
            OrderSortOption.OldestFirst => "Oldest order date",
            OrderSortOption.NewestCreated => "Newest added",
            OrderSortOption.OldestCreated => "Oldest added",
            OrderSortOption.ExpectedSoonest => "Expected soonest",
            OrderSortOption.TotalHighToLow => "Total: high to low",
            OrderSortOption.TotalLowToHigh => "Total: low to high",
            AccountSortOption.NameAscending or ItemSortOption.NameAscending => "Name A-Z",
            AccountSortOption.NameDescending or ItemSortOption.NameDescending => "Name Z-A",
            AccountSortOption.EmailAscending => "Email A-Z",
            AccountSortOption.EmailDescending => "Email Z-A",
            AccountSortOption.MerchantAscending or ItemSortOption.MerchantAscending => "Merchant A-Z",
            AccountSortOption.FavoritesFirst or ItemSortOption.FavoritesFirst => "Favorites first",
            AccountSortOption.MostUsed => "Most used",
            AccountSortOption.LeastUsed => "Least used",
            ItemSortOption.MostUsed => "Most saved uses",
            ItemSortOption.LeastUsed => "Least saved uses",
            AccountSortOption.NewestCreated => "Newest added",
            AccountSortOption.OldestCreated => "Oldest added",
            ItemSortOption.CategoryAscending => "Category A-Z",
            ItemSortOption.PriceLowToHigh => "Price: low to high",
            ItemSortOption.PriceHighToLow => "Price: high to low",
            ItemSortOption.QuantityLowToHigh => "Quantity: low to high",
            ItemSortOption.QuantityHighToLow => "Quantity: high to low",
            OrderAttentionFilter.All => "All active orders",
            OrderAttentionFilter.Overdue => "Past expected",
            OrderAttentionFilter.MissingTracking => "Missing tracking",
            OrderAttentionFilter.ReadyToArchive => "Ready to archive",
            UiDensity.Comfortable => "Comfortable",
            UiDensity.Compact => "Compact",
            Enum enumValue => EnumDisplayFormatter.Format(enumValue),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
