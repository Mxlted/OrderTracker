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
    NewestFirst = 0,
    OldestFirst = 1,
    ExpectedSoonest = 2,
    Merchant = 3,
    Account = 4,
    Item = 5,
    Status = 6,
    TotalHighToLow = 7,
    TotalLowToHigh = 8,
    NewestCreated = 9,
    OldestCreated = 10
}

public enum AccountGroupOption
{
    None,
    Merchant,
    Favorite,
    Usage,
    EmailDomain
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

public enum ItemGroupOption
{
    None,
    Category,
    Merchant,
    Favorite,
    Usage,
    PriceRange
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
