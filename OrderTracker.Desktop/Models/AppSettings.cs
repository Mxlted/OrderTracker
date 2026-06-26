using System.Collections.ObjectModel;
using System.Linq;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class AppSettings : ObservableObject
{
    public const decimal DefaultProjectedRoiPercent = 10m;

    public static IReadOnlyList<MerchantKind> ListedMerchants { get; } = Enum.GetValues<MerchantKind>();

    private BrowserPreference _browserPreference = BrowserPreference.Default;
    private string _customBrowserPath = string.Empty;
    private bool _useAccountBrowserSessions = true;
    private bool _discordEnabled;
    private string _discordWebhookUrl = string.Empty;
    private string _defaultAccountEmail = string.Empty;
    private MerchantKind _defaultMerchant = MerchantKind.Unknown;
    private bool _autoSave = true;
    private AppTheme _theme = AppTheme.Dark;
    private OrderGroupOption _orderGroup = OrderGroupOption.None;
    private double _windowWidth;
    private double _windowHeight;
    private double? _windowLeft;
    private double? _windowTop;
    private bool _isWindowMaximized;
    private double? _browserLinkWindowWidth;
    private double? _browserLinkWindowHeight;
    private double? _browserLinkWindowLeft;
    private double? _browserLinkWindowTop;

    public BrowserPreference BrowserPreference
    {
        get => _browserPreference;
        set => SetProperty(ref _browserPreference, value);
    }

    public string CustomBrowserPath
    {
        get => _customBrowserPath;
        set => SetProperty(ref _customBrowserPath, value ?? string.Empty);
    }

    public bool UseAccountBrowserSessions
    {
        get => _useAccountBrowserSessions;
        set => SetProperty(ref _useAccountBrowserSessions, value);
    }

    public bool DiscordEnabled
    {
        get => _discordEnabled;
        set => SetProperty(ref _discordEnabled, value);
    }

    public string DiscordWebhookUrl
    {
        get => _discordWebhookUrl;
        set => SetProperty(ref _discordWebhookUrl, value ?? string.Empty);
    }

    public string DefaultAccountEmail
    {
        get => _defaultAccountEmail;
        set => SetProperty(ref _defaultAccountEmail, value ?? string.Empty);
    }

    public MerchantKind DefaultMerchant
    {
        get => _defaultMerchant;
        set => SetProperty(ref _defaultMerchant, value);
    }

    public bool AutoSave
    {
        get => _autoSave;
        set => SetProperty(ref _autoSave, value);
    }

    public AppTheme Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public OrderGroupOption OrderGroup
    {
        get => _orderGroup;
        set => SetProperty(ref _orderGroup, value);
    }

    public double WindowWidth
    {
        get => _windowWidth;
        set => SetProperty(ref _windowWidth, value);
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set => SetProperty(ref _windowHeight, value);
    }

    public double? WindowLeft
    {
        get => _windowLeft;
        set => SetProperty(ref _windowLeft, value);
    }

    public double? WindowTop
    {
        get => _windowTop;
        set => SetProperty(ref _windowTop, value);
    }

    public bool IsWindowMaximized
    {
        get => _isWindowMaximized;
        set => SetProperty(ref _isWindowMaximized, value);
    }

    public double? BrowserLinkWindowWidth
    {
        get => _browserLinkWindowWidth;
        set => SetProperty(ref _browserLinkWindowWidth, value);
    }

    public double? BrowserLinkWindowHeight
    {
        get => _browserLinkWindowHeight;
        set => SetProperty(ref _browserLinkWindowHeight, value);
    }

    public double? BrowserLinkWindowLeft
    {
        get => _browserLinkWindowLeft;
        set => SetProperty(ref _browserLinkWindowLeft, value);
    }

    public double? BrowserLinkWindowTop
    {
        get => _browserLinkWindowTop;
        set => SetProperty(ref _browserLinkWindowTop, value);
    }

    public ColumnSettings Columns { get; set; } = new();

    public ObservableCollection<MerchantRoiSetting> MerchantProjectedRoiPercents { get; set; } = CreateDefaultMerchantProjectedRoiPercents();

    public decimal GetProjectedRoiPercent(MerchantKind merchant)
    {
        return MerchantProjectedRoiPercents.FirstOrDefault(setting => setting.Merchant == merchant)?.ProjectedRoiPercent
            ?? DefaultProjectedRoiPercent;
    }

    public decimal GetProjectedRoiPercent(Order order)
    {
        return order.ProjectedRoiPercentOverride ?? GetProjectedRoiPercent(order.Merchant);
    }

    public static ObservableCollection<MerchantRoiSetting> CreateDefaultMerchantProjectedRoiPercents()
    {
        return new ObservableCollection<MerchantRoiSetting>(
            ListedMerchants
                .Select(merchant => new MerchantRoiSetting
                {
                    Merchant = merchant,
                    ProjectedRoiPercent = DefaultProjectedRoiPercent
                }));
    }
}
