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
    Unknown,
    Amazon,
    Walmart,
    Target,
    BestBuy,
    eBay,
    Etsy,
    Other
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
