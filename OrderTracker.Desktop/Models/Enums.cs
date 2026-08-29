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

public enum UiDensity
{
    Comfortable,
    Compact
}

public enum OrderAttentionFilter
{
    All,
    Overdue,
    ExpectedToday,
    MissingTracking,
    ReadyToArchive
}

public enum CarrierKind
{
    Unknown,
    UPS,
    FedEx,
    USPS,
    Amazon,
    DHL,
    OnTrac
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
    NameAscending = 0,
    NameDescending = 1,
    EmailAscending = 2,
    EmailDescending = 3,
    MerchantAscending = 4,
    FavoritesFirst = 5,
    MostUsed = 6,
    LeastUsed = 7,
    NewestCreated = 8,
    OldestCreated = 9
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
