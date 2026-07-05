namespace OrderTracker.Desktop.Models;

public enum AppPage
{
    Dashboard,
    Orders,
    Archive,
    Accounts,
    Presets,
    Settings
}

public enum BrowserPreference
{
    Default,
    Chrome,
    Edge,
    Brave,
    Firefox,
    Custom
}

public enum AppTheme
{
    Light,
    Dark,
    OLED
}

public enum CarrierKind
{
    Unknown,
    UPS,
    FedEx,
    USPS,
    Amazon
}

public enum MerchantKind
{
    Unknown = 0,
    Amazon = 1,
    Walmart = 2,
    Target = 3,
    BestBuy = 4,
    eBay = 5,
    Other = 7
}

public enum OrderGroupOption
{
    None,
    Account,
    Item,
    Merchant,
    Status,
    Month,
    Year
}

public enum OrderSortOption
{
    NewestFirst,
    OldestFirst,
    ExpectedSoonest,
    Merchant,
    Account,
    Item,
    Status,
    TotalHighToLow,
    TotalLowToHigh
}

public enum AccountSortOption
{
    NameAscending,
    NameDescending,
    EmailAscending,
    EmailDescending,
    MerchantAscending,
    FavoritesFirst,
    MostUsed,
    LeastUsed
}

public enum ItemSortOption
{
    NameAscending,
    NameDescending,
    CategoryAscending,
    MerchantAscending,
    FavoritesFirst,
    MostUsed,
    LeastUsed,
    PriceLowToHigh,
    PriceHighToLow,
    QuantityLowToHigh,
    QuantityHighToLow
}

public enum OrderStatus
{
    Ordered,
    Processing,
    Shipped,
    OutForDelivery,
    Delivered,
    Delayed,
    Cancelled,
    Returned
}
