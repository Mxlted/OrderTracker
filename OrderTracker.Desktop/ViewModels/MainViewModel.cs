using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using OrderTracker.Desktop.Commands;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Services;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    [Flags]
    private enum PresetRefreshScope
    {
        None = 0,
        Accounts = 1,
        Items = 2
    }

    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan SidebarClockDisplayOffset = TimeSpan.FromSeconds(1);

    private readonly AppDataStore _dataStore = new();
    private readonly BrowserLauncher _browserLauncher = new();
    private readonly DiscordWebhookService _discordWebhookService = new();
    private readonly NetworkTimeService _networkTimeService = new();
    private readonly MerchantFaviconService _merchantFaviconService = new();
    private readonly HashSet<MerchantKind> _pendingMerchantFaviconFetches = new();
    private readonly HashSet<ItemPreset> _pendingAppliedItemPresets = new();
    private readonly Dictionary<Order, ObservableCollection<TrackingEntry>> _subscribedTrackingCollections = new();
    private readonly AppData _data;
    private readonly DispatcherTimer _autosaveTimer = new();
    private readonly DispatcherTimer _sidebarClockTimer = new();

    private AppPage _selectedPage = AppPage.Dashboard;
    private Order? _selectedOrder;
    private string _lastActionMessage = "Ready.";
    private DateTime _sidebarDateTime = DateTime.Now;
    private DateTimeOffset? _networkClockUtc;
    private long _networkClockTimestamp;
    private string _searchText = string.Empty;
    private string _archiveSearchText = string.Empty;
    private OrderGroupOption _selectedGroup = OrderGroupOption.None;
    private OrderSortOption _selectedSort = OrderSortOption.NewestFirst;
    private AccountGroupOption _selectedAccountGroup = AccountGroupOption.None;
    private AccountSortOption _selectedAccountSort = AccountSortOption.NameAscending;
    private ItemGroupOption _selectedItemGroup = ItemGroupOption.None;
    private ItemSortOption _selectedItemSort = ItemSortOption.MostUsed;
    private OrderAttentionFilter _selectedAttentionFilter = OrderAttentionFilter.All;
    private bool _hideCompleted;
    private string? _editingOrderId;
    private bool _isOrderEditorOpen;
    private AccountPreset? _selectedOrderAccountPreset;
    private AccountPreset? _selectedAccountPreset;
    private string? _editingAccountPresetId;
    private bool _isAccountPresetEditorOpen;
    private ItemPreset? _selectedOrderPreset;
    private ItemPreset? _selectedPreset;
    private string? _editingPresetId;
    private bool _isPresetEditorOpen;
    private bool _isBusy;
    private bool _suppressOrderChangeNotifications;
    private bool _suppressPresetChangeNotifications;
    private bool _suppressBulkSelectionNotifications;
    private bool _isFlushingPresetRefreshes;
    private PresetRefreshScope _pendingPresetRefreshes;
    private bool _isDisposed;
    private int _merchantIconCacheVersion;
    private bool _isFetchingMerchantFavicons;
    private bool _isConfirmationOpen;
    private bool _confirmationIsDanger;
    private string _confirmationTitle = string.Empty;
    private string _confirmationMessage = string.Empty;
    private string _confirmationConfirmText = "Confirm";
    private string _confirmationCancelText = "Cancel";
    private Action? _pendingConfirmationAction;
    private string? _pendingConfirmationCancelMessage;
    private bool _isAccountUsageAuditOpen;
    private AccountPreset? _accountUsageAuditPreset;
    private bool _isOrderAdvancedOpen;
    private bool _isAccountAdvancedOpen;
    private bool _isPresetAdvancedOpen;
    private bool _isDiscordWebhookRevealed;
    private string _settingsSaveStatus = "All changes saved.";

    private string _formAccountEmail = string.Empty;
    private MerchantKind _formMerchant = MerchantKind.Unknown;
    private string _formOrderNumber = string.Empty;
    private string _formOrderLink = string.Empty;
    private decimal _formShippingCost;
    private decimal _formTax;
    private decimal _formOtherCost;
    private string _formShippingCostInput = string.Empty;
    private string _formTaxInput = string.Empty;
    private string _formOtherCostInput = string.Empty;
    private decimal? _formProjectedRoiPercentOverride;
    private string _formProjectedRoiPercentInput = string.Empty;
    private decimal? _formProjectedProfitOverride;
    private string _formProjectedProfitInput = string.Empty;
    private DateTime? _formOrderDate = DateTime.Today;
    private DateTime? _formExpectedDate;
    private DateTime? _formDeliveredDate;
    private OrderStatus _formStatus = OrderStatus.Ordered;
    private string _formTrackingStatus = string.Empty;
    private string _formTrackingNumbersText = string.Empty;
    private string _formNotes = string.Empty;

    private string _presetName = string.Empty;
    private string _presetCategory = string.Empty;
    private MerchantKind _presetMerchantHint = MerchantKind.Unknown;
    private int _presetDefaultQuantity = 1;
    private decimal _presetDefaultUnitPrice;
    private decimal _presetDefaultShipping;
    private decimal _presetDefaultTax;
    private string _presetDefaultQuantityInput = string.Empty;
    private string _presetDefaultUnitPriceInput = string.Empty;
    private string _presetDefaultShippingInput = string.Empty;
    private string _presetDefaultTaxInput = string.Empty;
    private bool _presetIsFavorite;
    private string _presetNotes = string.Empty;
    private string _presetSearchText = string.Empty;

    private string _accountPresetName = string.Empty;
    private string _accountPresetEmail = string.Empty;
    private MerchantKind _accountPresetMerchantHint = MerchantKind.Unknown;
    private bool _accountPresetIsFavorite;
    private string _accountPresetNotes = string.Empty;
    private string _accountPresetSearchText = string.Empty;

    public MainViewModel()
    {
        _data = _dataStore.Load();
        _selectedGroup = Settings.OrderGroup;
        _selectedSort = Settings.OrderSort;
        _selectedAccountGroup = Settings.AccountGroup;
        _selectedAccountSort = Settings.AccountSort;
        _selectedItemGroup = Settings.ItemGroup;
        _selectedItemSort = Settings.ItemSort;
        OrdersView = new ListCollectionView(Orders);
        OrdersView.Filter = FilterOrder;
        ArchivedOrdersView = new ListCollectionView(Orders);
        ArchivedOrdersView.Filter = FilterArchivedOrder;
        AccountPresetsView = CollectionViewSource.GetDefaultView(AccountPresets);
        AccountPresetsView.Filter = FilterAccountPreset;
        OrderAccountPresetsView = new ListCollectionView(AccountPresets);
        OrderAccountPresetsView.Filter = FilterOrderAccountPreset;
        PresetsView = CollectionViewSource.GetDefaultView(ItemPresets);
        PresetsView.Filter = FilterPreset;
        OrderItemPresetsView = new ListCollectionView(ItemPresets);
        OrderItemPresetsView.SortDescriptions.Add(new SortDescription(nameof(ItemPreset.UsageCount), ListSortDirection.Descending));
        OrderItemPresetsView.SortDescriptions.Add(new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending));
        OrderItemPresetsView.Filter = FilterOrderItemPreset;

        Orders.CollectionChanged += OrdersCollectionChanged;
        foreach (var order in Orders)
        {
            SubscribeToOrder(order);
        }

        AccountPresets.CollectionChanged += AccountPresetsCollectionChanged;
        foreach (var preset in AccountPresets)
        {
            preset.PropertyChanged += AccountPresetPropertyChanged;
        }

        ItemPresets.CollectionChanged += ItemPresetsCollectionChanged;
        foreach (var preset in ItemPresets)
        {
            preset.PropertyChanged += ItemPresetPropertyChanged;
        }

        Settings.PropertyChanged += SettingsPropertyChanged;
        Settings.Columns.PropertyChanged += SettingsPropertyChanged;
        Settings.MerchantProjectedRoiPercents.CollectionChanged += MerchantProjectedRoiPercentsCollectionChanged;
        foreach (var setting in Settings.MerchantProjectedRoiPercents)
        {
            setting.PropertyChanged += MerchantProjectedRoiSettingPropertyChanged;
        }

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter?.ToString()), _ => !HasOpenModal);
        ToggleSidebarCommand = new RelayCommand(_ => Settings.IsSidebarCollapsed = !Settings.IsSidebarCollapsed);
        NewOrderCommand = new RelayCommand(_ => RequestNewOrder(), _ => !HasOpenModal);
        ToggleQuickOrderCommand = new RelayCommand(_ => ToggleQuickOrder(), _ => !HasOpenModal);
        EditOrderCommand = new RelayCommand(parameter => RequestEditOrder(parameter as Order), parameter => parameter is Order && !HasOpenModal);
        SaveOrderCommand = new RelayCommand(_ => SaveOrder(), _ => CanSaveOrder);
        CloseOrderEditorCommand = new RelayCommand(_ => CloseOrderEditor(), _ => IsOrderEditorOpen && !HasOpenModal);
        DeleteOrderCommand = new RelayCommand(parameter => DeleteOrder(parameter as Order), parameter => parameter is Order);
        DuplicateOrderCommand = new RelayCommand(parameter => DuplicateOrder(parameter as Order), parameter => parameter is Order);
        ToggleCompletedCommand = new RelayCommand(
            parameter => ToggleCompleted(parameter as Order),
            parameter => parameter is Order { CanToggleDelivered: true });
        PrimaryOrderActionCommand = new RelayCommand(
            parameter => RunPrimaryOrderAction(parameter as Order),
            parameter => parameter is Order { IsArchived: false, HasPrimaryAction: true });
        ArchiveCompletedOrdersCommand = new RelayCommand(_ => ArchiveCompletedOrders(), _ => CompletedOrdersReadyToArchiveCount > 0);
        RestoreOrderCommand = new RelayCommand(parameter => RestoreOrder(parameter as Order), parameter => parameter is Order { IsArchived: true });
        ClearSelectedOrdersCommand = new RelayCommand(_ => ClearOrderSelection(includeArchived: false), _ => SelectedActiveOrderCount > 0);
        MarkSelectedOrdersCompletedCommand = new RelayCommand(_ => MarkSelectedOrdersCompleted(), _ => SelectedIncompleteActiveOrderCount > 0);
        ArchiveSelectedCompletedOrdersCommand = new RelayCommand(_ => ArchiveSelectedCompletedOrders(), _ => SelectedCompletedActiveOrderCount > 0);
        DeleteSelectedOrdersCommand = new RelayCommand(_ => DeleteSelectedOrders(includeArchived: false), _ => SelectedActiveOrderCount > 0);
        ToggleVisibleOrderItemsExpansionCommand = new RelayCommand(_ => ToggleVisibleOrderItemsExpansion(), _ => HasVisibleOrderItems);
        ClearSelectedArchivedOrdersCommand = new RelayCommand(_ => ClearOrderSelection(includeArchived: true), _ => SelectedArchivedOrderCount > 0);
        RestoreSelectedOrdersCommand = new RelayCommand(_ => RestoreSelectedOrders(), _ => SelectedArchivedOrderCount > 0);
        DeleteSelectedArchivedOrdersCommand = new RelayCommand(_ => DeleteSelectedOrders(includeArchived: true), _ => SelectedArchivedOrderCount > 0);
        OpenOrderLinkCommand = new RelayCommand(parameter => OpenOrderLink(parameter as Order), parameter => parameter is Order);
        OpenTrackingCommand = new RelayCommand(parameter => OpenTracking(parameter as TrackingEntry), parameter => parameter is TrackingEntry);
        CopyTrackingNumbersCommand = new RelayCommand(parameter => CopyTrackingNumbers(parameter as Order), parameter => parameter is Order);
        CopyTextCommand = new RelayCommand(CopyText, HasTextToCopy);
        AddOrderItemCommand = new RelayCommand(_ => AddFormItem());
        RemoveOrderItemCommand = new RelayCommand(parameter => RemoveFormItem(parameter as OrderItem), parameter => parameter is OrderItem && FormItems.Count > 1);
        ApplyOrderAttentionFilterCommand = new RelayCommand(ApplyOrderAttentionFilter, _ => !HasOpenModal);
        ClearOrderAttentionFilterCommand = new RelayCommand(_ => SelectedAttentionFilter = OrderAttentionFilter.All, _ => HasAttentionFilter);
        SaveSettingsCommand = new RelayCommand(_ => SaveNow("Settings saved."));
        ToggleDiscordWebhookRevealCommand = new RelayCommand(_ => IsDiscordWebhookRevealed = !IsDiscordWebhookRevealed);
        ClearDiscordWebhookCommand = new RelayCommand(_ => ClearDiscordWebhook(), _ => !string.IsNullOrWhiteSpace(Settings.DiscordWebhookUrl));
        ClearMerchantIconCacheCommand = new RelayCommand(_ => ClearMerchantIconCache());
        MigrateLegacyOrderItemsCommand = new RelayCommand(_ => MigrateLegacyOrderItems(), _ => LegacyOrderItemMigrationCount > 0);
        SendDiscordStatsCommand = new RelayCommand(async _ => await SendDiscordStatsAsync(), _ => !IsBusy);
        ConfirmDialogCommand = new RelayCommand(_ => ConfirmDialog(), _ => IsConfirmationOpen);
        CancelDialogCommand = new RelayCommand(_ => CancelDialog(), _ => IsConfirmationOpen);

        NewAccountPresetCommand = new RelayCommand(_ => RequestNewAccountPreset(), _ => !HasOpenModal);
        ToggleQuickAccountPresetCommand = new RelayCommand(_ => ToggleQuickAccountPreset(), _ => !HasOpenModal);
        EditAccountPresetCommand = new RelayCommand(parameter => RequestEditAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset && !HasOpenModal);
        SaveAccountPresetCommand = new RelayCommand(_ => SaveAccountPreset(), _ => CanSaveAccountPreset);
        CloseAccountPresetEditorCommand = new RelayCommand(_ => CloseAccountPresetEditor(), _ => IsAccountPresetEditorOpen && !HasOpenModal);
        DeleteAccountPresetCommand = new RelayCommand(parameter => DeleteAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset);
        DuplicateAccountPresetCommand = new RelayCommand(parameter => DuplicateAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset);
        ApplyAccountPresetCommand = new RelayCommand(parameter => ApplyAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset);
        ViewAccountOrdersCommand = new RelayCommand(async parameter => await ViewAccountOrdersAsync(parameter as AccountPreset), parameter => CanViewAccountOrders(parameter as AccountPreset));
        ClearAccountSessionCommand = new RelayCommand(parameter => ClearAccountSession(parameter as AccountPreset), parameter => CanClearAccountSession(parameter as AccountPreset));
        ClearSelectedAccountPresetsCommand = new RelayCommand(_ => ClearAccountPresetSelection(), _ => SelectedAccountPresetCount > 0);
        DeleteSelectedAccountPresetsCommand = new RelayCommand(_ => DeleteSelectedAccountPresets(), _ => SelectedAccountPresetCount > 0);
        OpenAccountUsageAuditCommand = new RelayCommand(parameter => OpenAccountUsageAudit(parameter as AccountPreset), parameter => parameter is AccountPreset);
        CloseAccountUsageAuditCommand = new RelayCommand(_ => CloseAccountUsageAudit());
        OpenAccountUsageOrderCommand = new RelayCommand(parameter => OpenAccountUsageOrder(parameter as Order), parameter => parameter is Order);

        NewPresetCommand = new RelayCommand(_ => RequestNewPreset(), _ => !HasOpenModal);
        ToggleQuickPresetCommand = new RelayCommand(_ => ToggleQuickPreset(), _ => !HasOpenModal);
        EditPresetCommand = new RelayCommand(parameter => RequestEditPreset(parameter as ItemPreset), parameter => parameter is ItemPreset && !HasOpenModal);
        SavePresetCommand = new RelayCommand(_ => SavePreset(), _ => CanSavePreset);
        ClosePresetEditorCommand = new RelayCommand(_ => ClosePresetEditor(), _ => IsPresetEditorOpen && !HasOpenModal);
        DeletePresetCommand = new RelayCommand(parameter => DeletePreset(parameter as ItemPreset), parameter => parameter is ItemPreset);
        DuplicatePresetCommand = new RelayCommand(parameter => DuplicatePreset(parameter as ItemPreset), parameter => parameter is ItemPreset);
        ApplyPresetCommand = new RelayCommand(parameter => ApplyPreset(parameter as ItemPreset), parameter => parameter is ItemPreset);
        ClearSelectedPresetsCommand = new RelayCommand(_ => ClearItemPresetSelection(), _ => SelectedPresetCount > 0);
        DeleteSelectedPresetsCommand = new RelayCommand(_ => DeleteSelectedPresets(), _ => SelectedPresetCount > 0);
        SaveCurrentCommand = new RelayCommand(_ => SaveCurrent(), _ => CanSaveCurrent);
        CloseCurrentPanelCommand = new RelayCommand(_ => CloseCurrentPanel(), _ => CanCloseCurrentPanel);

        FormItems.CollectionChanged += FormItemsCollectionChanged;
        StartAutosaveTimer();
        ResetOrderForm();
        ResetAccountPresetForm();
        ResetPresetForm();
        var accountUsageChanged = RefreshAccountUsageCounts();
        ApplySortAndGroup();
        ApplyArchiveSort();
        ApplyAccountPresetSortAndGroup();
        ApplyItemPresetSortAndGroup();
        RefreshDashboard();
        RefreshArchiveState();
        RefreshMerchantIconCacheState();
        QueueMerchantFaviconFetch();
        StartSidebarClock();
        _ = SyncSidebarClockAsync();
        if (accountUsageChanged)
        {
            PersistIfNeeded();
        }
    }

    public AppSettings Settings => _data.Settings;

    public ObservableCollection<Order> Orders => _data.Orders;

    public ObservableCollection<AccountPreset> AccountPresets => _data.AccountPresets;

    public ObservableCollection<ItemPreset> ItemPresets => _data.ItemPresets;

    public ObservableCollection<Order> AccountUsageAuditOrders { get; } = new();

    public event Action<Order>? OrderRevealRequested;

    public ICollectionView OrdersView { get; }

    public ICollectionView ArchivedOrdersView { get; }

    public ICollectionView AccountPresetsView { get; }

    public ICollectionView OrderAccountPresetsView { get; }

    public ICollectionView PresetsView { get; }

    public ICollectionView OrderItemPresetsView { get; }

    public ObservableCollection<MetricCard> MetricCards { get; } = new();

    public ObservableCollection<ChartPoint> MerchantSpend { get; } = new();

    public ObservableCollection<ChartPoint> StatusBreakdown { get; } = new();

    public ObservableCollection<MonthlyComparisonPoint> MonthlyComparison { get; } = new();

    public ObservableCollection<SidebarPanelItem> SidebarAlerts { get; } = new();

    public ObservableCollection<OrderItem> FormItems { get; } = new();

    public Array Pages => Enum.GetValues<AppPage>();

    public IReadOnlyList<MerchantKind> Merchants => AppSettings.ListedMerchants;

    public Array OrderStatuses => Enum.GetValues<OrderStatus>();

    public Array GroupOptions => Enum.GetValues<OrderGroupOption>();

    public IReadOnlyList<OrderSortOption> SortOptions { get; } =
    [
        OrderSortOption.NewestFirst,
        OrderSortOption.OldestFirst,
        OrderSortOption.NewestCreated,
        OrderSortOption.OldestCreated,
        OrderSortOption.ExpectedSoonest,
        OrderSortOption.Merchant,
        OrderSortOption.Account,
        OrderSortOption.Item,
        OrderSortOption.Status,
        OrderSortOption.TotalHighToLow,
        OrderSortOption.TotalLowToHigh
    ];

    public Array AccountGroupOptions => Enum.GetValues<AccountGroupOption>();

    public Array AccountSortOptions => Enum.GetValues<AccountSortOption>();

    public Array ItemGroupOptions => Enum.GetValues<ItemGroupOption>();

    public Array ItemSortOptions => Enum.GetValues<ItemSortOption>();

    public Array BrowserOptions => Enum.GetValues<BrowserPreference>();

    public Array Themes => Enum.GetValues<AppTheme>();

    public Array Densities => Enum.GetValues<UiDensity>();

    public Array AttentionFilterOptions => Enum.GetValues<OrderAttentionFilter>();

    public IReadOnlyList<int> DashboardMonthRangeOptions { get; } = [3, 6, 12];

    public IEnumerable<MerchantRoiSetting> ActiveMerchantRoiSettings
    {
        get
        {
            var activeMerchants = Orders.Select(order => order.Merchant)
                .Where(merchant => merchant != MerchantKind.Unknown)
                .ToHashSet();
            return Settings.MerchantProjectedRoiPercents.Where(setting => activeMerchants.Contains(setting.Merchant));
        }
    }

    public IEnumerable<MerchantRoiSetting> InactiveMerchantRoiSettings
    {
        get
        {
            var active = ActiveMerchantRoiSettings.Select(setting => setting.Merchant).ToHashSet();
            return Settings.MerchantProjectedRoiPercents.Where(setting => !active.Contains(setting.Merchant));
        }
    }

    public ICommand NavigateCommand { get; }

    public ICommand ToggleSidebarCommand { get; }

    public ICommand NewOrderCommand { get; }

    public ICommand ToggleQuickOrderCommand { get; }

    public ICommand EditOrderCommand { get; }

    public ICommand SaveOrderCommand { get; }

    public ICommand CloseOrderEditorCommand { get; }

    public ICommand DeleteOrderCommand { get; }

    public ICommand DuplicateOrderCommand { get; }

    public ICommand ToggleCompletedCommand { get; }

    public ICommand PrimaryOrderActionCommand { get; }

    public ICommand ArchiveCompletedOrdersCommand { get; }

    public ICommand RestoreOrderCommand { get; }

    public ICommand ClearSelectedOrdersCommand { get; }

    public ICommand MarkSelectedOrdersCompletedCommand { get; }

    public ICommand ArchiveSelectedCompletedOrdersCommand { get; }

    public ICommand DeleteSelectedOrdersCommand { get; }

    public ICommand ToggleVisibleOrderItemsExpansionCommand { get; }

    public ICommand ClearSelectedArchivedOrdersCommand { get; }

    public ICommand RestoreSelectedOrdersCommand { get; }

    public ICommand DeleteSelectedArchivedOrdersCommand { get; }

    public ICommand OpenOrderLinkCommand { get; }

    public ICommand OpenTrackingCommand { get; }

    public ICommand CopyTrackingNumbersCommand { get; }

    public ICommand CopyTextCommand { get; }

    public ICommand AddOrderItemCommand { get; }

    public ICommand RemoveOrderItemCommand { get; }

    public ICommand ApplyOrderAttentionFilterCommand { get; }

    public ICommand ClearOrderAttentionFilterCommand { get; }

    public ICommand SaveSettingsCommand { get; }

    public ICommand ToggleDiscordWebhookRevealCommand { get; }

    public ICommand ClearDiscordWebhookCommand { get; }

    public ICommand ClearMerchantIconCacheCommand { get; }

    public ICommand MigrateLegacyOrderItemsCommand { get; }

    public ICommand SendDiscordStatsCommand { get; }

    public ICommand ConfirmDialogCommand { get; }

    public ICommand CancelDialogCommand { get; }

    public ICommand NewAccountPresetCommand { get; }

    public ICommand ToggleQuickAccountPresetCommand { get; }

    public ICommand EditAccountPresetCommand { get; }

    public ICommand SaveAccountPresetCommand { get; }

    public ICommand CloseAccountPresetEditorCommand { get; }

    public ICommand DeleteAccountPresetCommand { get; }

    public ICommand DuplicateAccountPresetCommand { get; }

    public ICommand ApplyAccountPresetCommand { get; }

    public ICommand ViewAccountOrdersCommand { get; }

    public ICommand ClearAccountSessionCommand { get; }

    public ICommand ClearSelectedAccountPresetsCommand { get; }

    public ICommand DeleteSelectedAccountPresetsCommand { get; }

    public ICommand OpenAccountUsageAuditCommand { get; }

    public ICommand CloseAccountUsageAuditCommand { get; }

    public ICommand OpenAccountUsageOrderCommand { get; }

    public ICommand NewPresetCommand { get; }

    public ICommand ToggleQuickPresetCommand { get; }

    public ICommand EditPresetCommand { get; }

    public ICommand SavePresetCommand { get; }

    public ICommand ClosePresetEditorCommand { get; }

    public ICommand DeletePresetCommand { get; }

    public ICommand DuplicatePresetCommand { get; }

    public ICommand ApplyPresetCommand { get; }

    public ICommand ClearSelectedPresetsCommand { get; }

    public ICommand DeleteSelectedPresetsCommand { get; }

    public ICommand SaveCurrentCommand { get; }

    public ICommand CloseCurrentPanelCommand { get; }

    public AppPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            CloseEditorsExcept(value);
            if (SetProperty(ref _selectedPage, value))
            {
                RefreshCurrentCommandState();
            }
        }
    }

    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set => SetProperty(ref _selectedOrder, value);
    }

    public string LastActionMessage
    {
        get => _lastActionMessage;
        set => SetProperty(ref _lastActionMessage, value ?? string.Empty);
    }

    public string SidebarDate => _sidebarDateTime.ToString("dddd, MMM d", CultureInfo.CurrentCulture);

    public string SidebarTime => _sidebarDateTime.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);

    public int ActiveOrderCount => Orders.Count(order => !order.IsArchived);

    public int AccountPresetCount => AccountPresets.Count;

    public int ItemPresetCount => ItemPresets.Count;

    public string OrdersNavLabel => $"Orders  {ActiveOrderCount}";

    public string ArchiveNavLabel => $"Archive  {ArchivedOrderCount}";

    public string AccountsNavLabel => $"Accounts  {AccountPresetCount}";

    public string ItemsNavLabel => $"Items  {ItemPresetCount}";

    public double SidebarWidth => Settings.IsSidebarCollapsed ? 72d : 236d;

    public bool IsSidebarExpanded => !Settings.IsSidebarCollapsed;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OrdersView.Refresh();
                RefreshItemExpansionToggleState();
                OnPropertyChanged(nameof(AreAllVisibleOrdersSelected));
                OnPropertyChanged(nameof(ShowOrderGroupHeaders));
            }
        }
    }

    public string ArchiveSearchText
    {
        get => _archiveSearchText;
        set
        {
            if (SetProperty(ref _archiveSearchText, value ?? string.Empty))
            {
                ArchivedOrdersView.Refresh();
                OnPropertyChanged(nameof(AreAllVisibleArchivedOrdersSelected));
            }
        }
    }

    public OrderGroupOption SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                Settings.OrderGroup = value;
                ApplySortAndGroup();
                OnPropertyChanged(nameof(ShowOrderMerchantColumn));
            }
        }
    }

    public OrderAttentionFilter SelectedAttentionFilter
    {
        get => _selectedAttentionFilter;
        set
        {
            if (SetProperty(ref _selectedAttentionFilter, value))
            {
                OrdersView.Refresh();
                OnPropertyChanged(nameof(HasAttentionFilter));
                OnPropertyChanged(nameof(AttentionFilterSummary));
                ((RelayCommand)ClearOrderAttentionFilterCommand).RaiseCanExecuteChanged();
                RefreshItemExpansionToggleState();
                OnPropertyChanged(nameof(AreAllVisibleOrdersSelected));
                OnPropertyChanged(nameof(ShowOrderGroupHeaders));
            }
        }
    }

    public bool HasAttentionFilter => SelectedAttentionFilter != OrderAttentionFilter.All;

    public string AttentionFilterSummary => SelectedAttentionFilter switch
    {
        OrderAttentionFilter.Overdue => "Showing orders past their expected date",
        OrderAttentionFilter.ExpectedToday => "Showing open orders expected today",
        OrderAttentionFilter.MissingTracking => "Showing open orders without tracking",
        OrderAttentionFilter.ReadyToArchive => "Showing delivered orders ready to archive",
        _ => "All active orders"
    };

    public OrderSortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                Settings.OrderSort = value;
                ApplySortAndGroup();
            }
        }
    }

    public AccountGroupOption SelectedAccountGroup
    {
        get => _selectedAccountGroup;
        set
        {
            if (SetProperty(ref _selectedAccountGroup, value))
            {
                Settings.AccountGroup = value;
                ApplyAccountPresetSortAndGroup();
                OnPropertyChanged(nameof(ShowAccountMerchantColumn));
            }
        }
    }

    public AccountSortOption SelectedAccountSort
    {
        get => _selectedAccountSort;
        set
        {
            if (SetProperty(ref _selectedAccountSort, value))
            {
                Settings.AccountSort = value;
                ApplyAccountPresetSortAndGroup();
            }
        }
    }

    public ItemGroupOption SelectedItemGroup
    {
        get => _selectedItemGroup;
        set
        {
            if (SetProperty(ref _selectedItemGroup, value))
            {
                Settings.ItemGroup = value;
                ApplyItemPresetSortAndGroup();
                OnPropertyChanged(nameof(ShowItemMerchantColumn));
            }
        }
    }

    public ItemSortOption SelectedItemSort
    {
        get => _selectedItemSort;
        set
        {
            if (SetProperty(ref _selectedItemSort, value))
            {
                Settings.ItemSort = value;
                ApplyItemPresetSortAndGroup();
            }
        }
    }

    public bool HideCompleted
    {
        get => _hideCompleted;
        set
        {
            if (SetProperty(ref _hideCompleted, value))
            {
                OrdersView.Refresh();
                RefreshItemExpansionToggleState();
                OnPropertyChanged(nameof(AreAllVisibleOrdersSelected));
                OnPropertyChanged(nameof(ShowOrderGroupHeaders));
            }
        }
    }

    public bool ShowOrderMerchantColumn => Settings.Columns.ShowMerchant && SelectedGroup != OrderGroupOption.Merchant;

    public bool ShowAccountMerchantColumn => SelectedAccountGroup != AccountGroupOption.Merchant;

    public bool ShowItemMerchantColumn => SelectedItemGroup != ItemGroupOption.Merchant;

    public bool ShowOrderGroupHeaders => SelectedGroup != OrderGroupOption.None && OrdersView.Groups?.Count > 1;

    public bool ShowAccountGroupHeaders => SelectedAccountGroup != AccountGroupOption.None && AccountPresetsView.Groups?.Count > 1;

    public bool ShowItemGroupHeaders => SelectedItemGroup != ItemGroupOption.None && PresetsView.Groups?.Count > 1;

    public int CompletedOrdersReadyToArchiveCount => Orders.Count(order => order.CanArchive && !order.IsArchived);

    public int ArchivedOrderCount => Orders.Count(order => order.IsArchived);

    public string ArchiveCompletedOrdersLabel => CompletedOrdersReadyToArchiveCount switch
    {
        0 => "Archive completed orders",
        1 => "Archive 1 completed order",
        var count => $"Archive {count} completed orders"
    };

    public string ArchivedOrderSummary => ArchivedOrderCount == 1
        ? "1 archived order is stored off the active list."
        : $"{ArchivedOrderCount} archived orders are stored off the active list.";

    public int SelectedActiveOrderCount => Orders.Count(order => order.IsSelected && !order.IsArchived);

    public int SelectedIncompleteActiveOrderCount => Orders.Count(order => order.IsSelected && !order.IsArchived && order.CanMarkDelivered);

    public int SelectedCompletedActiveOrderCount => Orders.Count(order => order.IsSelected && !order.IsArchived && order.CanArchive);

    public int SelectedArchivedOrderCount => Orders.Count(order => order.IsSelected && order.IsArchived);

    public int SelectedAccountPresetCount => AccountPresets.Count(preset => preset.IsSelected);

    public int SelectedPresetCount => ItemPresets.Count(preset => preset.IsSelected);

    public bool HasSelectedActiveOrders => SelectedActiveOrderCount > 0;

    public bool HasSelectedArchivedOrders => SelectedArchivedOrderCount > 0;

    public bool HasSelectedAccountPresets => SelectedAccountPresetCount > 0;

    public bool HasSelectedPresets => SelectedPresetCount > 0;

    public bool? AreAllVisibleOrdersSelected
    {
        get => GetVisibleSelectionState(OrdersView, item => item is Order { IsArchived: false } order && order.IsSelected);
        set => SelectOrders(OrdersView, AreAllVisibleOrdersSelected != true);
    }

    public bool? AreAllVisibleArchivedOrdersSelected
    {
        get => GetVisibleSelectionState(ArchivedOrdersView, item => item is Order { IsArchived: true } order && order.IsSelected);
        set => SelectOrders(ArchivedOrdersView, AreAllVisibleArchivedOrdersSelected != true);
    }

    public bool? AreAllVisibleAccountPresetsSelected
    {
        get => GetVisibleSelectionState(AccountPresetsView, item => item is AccountPreset preset && preset.IsSelected);
        set => SelectAccountPresets(AccountPresetsView, AreAllVisibleAccountPresetsSelected != true);
    }

    public bool? AreAllVisiblePresetsSelected
    {
        get => GetVisibleSelectionState(PresetsView, item => item is ItemPreset preset && preset.IsSelected);
        set => SelectItemPresets(PresetsView, AreAllVisiblePresetsSelected != true);
    }

    public string ActiveOrderBulkSelectionSummary => FormatSelectedCount(SelectedActiveOrderCount, "active order");

    public bool HasVisibleOrderItems => GetVisibleOrdersWithItems().Any();

    public string VisibleOrderItemsExpansionToggleLabel => ShouldCollapseVisibleOrderItems()
        ? "Collapse"
        : "Expand";

    public string VisibleOrderItemsExpansionToggleToolTip => ShouldCollapseVisibleOrderItems()
        ? "Collapse all visible item details"
        : "Expand all visible item details";

    public string ArchivedOrderBulkSelectionSummary => FormatSelectedCount(SelectedArchivedOrderCount, "archived order");

    public string AccountPresetBulkSelectionSummary => FormatSelectedCount(SelectedAccountPresetCount, "account");

    public string PresetBulkSelectionSummary => FormatSelectedCount(SelectedPresetCount, "item");

    public bool IsOrderEditorOpen
    {
        get => _isOrderEditorOpen;
        private set
        {
            if (SetProperty(ref _isOrderEditorOpen, value))
            {
                RefreshEditorCommandState();
            }
        }
    }

    public bool IsEditingOrder => !string.IsNullOrWhiteSpace(_editingOrderId);

    public string OrderEditorTitle => IsEditingOrder ? "Edit order" : "New order";

    public bool CanSaveOrder => IsOrderEditorOpen && !HasOpenModal && FormItems.Any(item => !string.IsNullOrWhiteSpace(item.Name));

    public bool IsOrderAdvancedOpen
    {
        get => _isOrderAdvancedOpen;
        set => SetProperty(ref _isOrderAdvancedOpen, value);
    }

    public decimal FormSubtotal => FormItems.Sum(item => ParseQuantityPreview(item.QuantityInput) * ParseMoneyPreview(item.UnitPriceInput));

    public decimal FormTotal => FormSubtotal +
        ParseMoneyPreview(FormShippingCostInput) +
        ParseMoneyPreview(FormTaxInput) +
        ParseMoneyPreview(FormOtherCostInput);

    public decimal FormEffectiveRoiPercent
    {
        get
        {
            var projectedProfitOverride = ParseOptionalMoneyPreview(FormProjectedProfitInput);
            return projectedProfitOverride.HasValue
                ? AppSettings.CalculateEffectiveProjectedRoiPercent(FormTotal, projectedProfitOverride.Value)
                : ParseOptionalPercentPreview(FormProjectedRoiPercentInput) ?? Settings.GetProjectedRoiPercent(FormMerchant);
        }
    }

    public decimal FormProjectedProfit => ParseOptionalMoneyPreview(FormProjectedProfitInput) ??
        AppSettings.CalculateProjectedRoiAmount(FormTotal, FormEffectiveRoiPercent);

    public string FormProjectionSummary => $"Projected profit {FormProjectedProfit.ToString("C", CultureInfo.CurrentCulture)} at {FormEffectiveRoiPercent.ToString("0.##", CultureInfo.CurrentCulture)}%";

    public bool HasFormCostDetails => ParseMoneyPreview(FormShippingCostInput) > 0m || ParseMoneyPreview(FormTaxInput) > 0m;

    public string FormCostDetailSummary
    {
        get
        {
            var details = new List<string>();
            var shipping = ParseMoneyPreview(FormShippingCostInput);
            var tax = ParseMoneyPreview(FormTaxInput);
            if (shipping > 0m)
            {
                details.Add($"Shipping {shipping.ToString("C", CultureInfo.CurrentCulture)}");
            }

            if (tax > 0m)
            {
                details.Add($"Tax {tax.ToString("C", CultureInfo.CurrentCulture)}");
            }

            return details.Count == 0 ? string.Empty : $"Includes {string.Join(" · ", details)}";
        }
    }

    public AccountPreset? SelectedOrderAccountPreset
    {
        get => _selectedOrderAccountPreset;
        set
        {
            if (SetProperty(ref _selectedOrderAccountPreset, value) && value is not null)
            {
                ApplyAccountPreset(value);
            }
        }
    }

    public ItemPreset? SelectedOrderPreset
    {
        get => _selectedOrderPreset;
        set
        {
            if (SetProperty(ref _selectedOrderPreset, value) && value is not null)
            {
                ApplyPreset(value);
                _selectedOrderPreset = null;
                OnPropertyChanged();
            }
        }
    }

    public string FormAccountEmail
    {
        get => _formAccountEmail;
        set => SetProperty(ref _formAccountEmail, value ?? string.Empty);
    }

    public MerchantKind FormMerchant
    {
        get => _formMerchant;
        set
        {
            if (SetProperty(ref _formMerchant, value))
            {
                RefreshOrderAccountPresets();
                OrderItemPresetsView.Refresh();
                RefreshOrderPreview();
            }
        }
    }

    public string FormOrderNumber
    {
        get => _formOrderNumber;
        set => SetProperty(ref _formOrderNumber, value ?? string.Empty);
    }

    public string FormOrderLink
    {
        get => _formOrderLink;
        set => SetProperty(ref _formOrderLink, value ?? string.Empty);
    }

    public decimal FormShippingCost
    {
        get => _formShippingCost;
        set
        {
            SetProperty(ref _formShippingCost, value);
            FormShippingCostInput = FormatMoneyInput(value);
        }
    }

    public decimal FormTax
    {
        get => _formTax;
        set
        {
            SetProperty(ref _formTax, value);
            FormTaxInput = FormatMoneyInput(value);
        }
    }

    public decimal FormOtherCost
    {
        get => _formOtherCost;
        set
        {
            SetProperty(ref _formOtherCost, value);
            FormOtherCostInput = FormatMoneyInput(value);
        }
    }

    public string FormShippingCostInput
    {
        get => _formShippingCostInput;
        set
        {
            if (SetProperty(ref _formShippingCostInput, value ?? string.Empty))
            {
                RefreshOrderPreview();
            }
        }
    }

    public string FormTaxInput
    {
        get => _formTaxInput;
        set
        {
            if (SetProperty(ref _formTaxInput, value ?? string.Empty))
            {
                RefreshOrderPreview();
            }
        }
    }

    public string FormOtherCostInput
    {
        get => _formOtherCostInput;
        set
        {
            if (SetProperty(ref _formOtherCostInput, value ?? string.Empty))
            {
                RefreshOrderPreview();
            }
        }
    }

    public decimal? FormProjectedRoiPercentOverride
    {
        get => _formProjectedRoiPercentOverride;
        set
        {
            var normalized = value.HasValue ? Math.Max(0m, value.Value) : (decimal?)null;
            SetProperty(ref _formProjectedRoiPercentOverride, normalized);
            FormProjectedRoiPercentInput = FormatPercentInput(normalized);
        }
    }

    public string FormProjectedRoiPercentInput
    {
        get => _formProjectedRoiPercentInput;
        set
        {
            if (SetProperty(ref _formProjectedRoiPercentInput, value ?? string.Empty))
            {
                RefreshOrderPreview();
            }
        }
    }

    public decimal? FormProjectedProfitOverride
    {
        get => _formProjectedProfitOverride;
        set
        {
            var normalized = value.HasValue ? Math.Max(0m, value.Value) : (decimal?)null;
            SetProperty(ref _formProjectedProfitOverride, normalized);
            FormProjectedProfitInput = FormatOptionalMoneyInput(normalized);
        }
    }

    public string FormProjectedProfitInput
    {
        get => _formProjectedProfitInput;
        set
        {
            if (SetProperty(ref _formProjectedProfitInput, value ?? string.Empty))
            {
                RefreshOrderPreview();
            }
        }
    }

    public DateTime? FormOrderDate
    {
        get => _formOrderDate;
        set => SetProperty(ref _formOrderDate, value);
    }

    public DateTime? FormExpectedDate
    {
        get => _formExpectedDate;
        set => SetProperty(ref _formExpectedDate, value);
    }

    public DateTime? FormDeliveredDate
    {
        get => _formDeliveredDate;
        set => SetProperty(ref _formDeliveredDate, value);
    }

    public OrderStatus FormStatus
    {
        get => _formStatus;
        set => SetProperty(ref _formStatus, value);
    }

    public string FormTrackingStatus
    {
        get => _formTrackingStatus;
        set => SetProperty(ref _formTrackingStatus, value ?? string.Empty);
    }

    public string FormTrackingNumbersText
    {
        get => _formTrackingNumbersText;
        set => SetProperty(ref _formTrackingNumbersText, value ?? string.Empty);
    }

    public string FormNotes
    {
        get => _formNotes;
        set => SetProperty(ref _formNotes, value ?? string.Empty);
    }

    public ItemPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public string PresetSearchText
    {
        get => _presetSearchText;
        set
        {
            if (SetProperty(ref _presetSearchText, value ?? string.Empty))
            {
                PresetsView.Refresh();
                OnPropertyChanged(nameof(ShowItemGroupHeaders));
                OnPropertyChanged(nameof(AreAllVisiblePresetsSelected));
            }
        }
    }

    public bool IsEditingPreset => !string.IsNullOrWhiteSpace(_editingPresetId);

    public bool IsPresetEditorOpen
    {
        get => _isPresetEditorOpen;
        private set
        {
            if (SetProperty(ref _isPresetEditorOpen, value))
            {
                RefreshEditorCommandState();
            }
        }
    }

    public string PresetEditorTitle => IsEditingPreset ? "Edit item" : "New item";

    public bool CanSavePreset => IsPresetEditorOpen && !HasOpenModal && !string.IsNullOrWhiteSpace(PresetName);

    public bool IsPresetAdvancedOpen
    {
        get => _isPresetAdvancedOpen;
        set => SetProperty(ref _isPresetAdvancedOpen, value);
    }

    public string PresetName
    {
        get => _presetName;
        set
        {
            if (SetProperty(ref _presetName, value ?? string.Empty))
            {
                ((RelayCommand)SavePresetCommand).RaiseCanExecuteChanged();
                RefreshCurrentCommandState();
            }
        }
    }

    public string PresetCategory
    {
        get => _presetCategory;
        set => SetProperty(ref _presetCategory, value ?? string.Empty);
    }

    public MerchantKind PresetMerchantHint
    {
        get => _presetMerchantHint;
        set => SetProperty(ref _presetMerchantHint, value);
    }

    public int PresetDefaultQuantity
    {
        get => _presetDefaultQuantity;
        set
        {
            var normalized = Math.Max(1, value);
            SetProperty(ref _presetDefaultQuantity, normalized);
            PresetDefaultQuantityInput = FormatQuantityInput(normalized);
        }
    }

    public decimal PresetDefaultUnitPrice
    {
        get => _presetDefaultUnitPrice;
        set
        {
            SetProperty(ref _presetDefaultUnitPrice, value);
            PresetDefaultUnitPriceInput = FormatMoneyInput(value);
        }
    }

    public decimal PresetDefaultShipping
    {
        get => _presetDefaultShipping;
        set
        {
            SetProperty(ref _presetDefaultShipping, value);
            PresetDefaultShippingInput = FormatMoneyInput(value);
        }
    }

    public decimal PresetDefaultTax
    {
        get => _presetDefaultTax;
        set
        {
            SetProperty(ref _presetDefaultTax, value);
            PresetDefaultTaxInput = FormatMoneyInput(value);
        }
    }

    public string PresetDefaultQuantityInput
    {
        get => _presetDefaultQuantityInput;
        set => SetProperty(ref _presetDefaultQuantityInput, value ?? string.Empty);
    }

    public string PresetDefaultUnitPriceInput
    {
        get => _presetDefaultUnitPriceInput;
        set => SetProperty(ref _presetDefaultUnitPriceInput, value ?? string.Empty);
    }

    public string PresetDefaultShippingInput
    {
        get => _presetDefaultShippingInput;
        set => SetProperty(ref _presetDefaultShippingInput, value ?? string.Empty);
    }

    public string PresetDefaultTaxInput
    {
        get => _presetDefaultTaxInput;
        set => SetProperty(ref _presetDefaultTaxInput, value ?? string.Empty);
    }

    public bool PresetIsFavorite
    {
        get => _presetIsFavorite;
        set => SetProperty(ref _presetIsFavorite, value);
    }

    public string PresetNotes
    {
        get => _presetNotes;
        set => SetProperty(ref _presetNotes, value ?? string.Empty);
    }

    public AccountPreset? SelectedAccountPreset
    {
        get => _selectedAccountPreset;
        set
        {
            if (SetProperty(ref _selectedAccountPreset, value))
            {
                RaiseAccountActionCommandState();
            }
        }
    }

    public string AccountPresetSearchText
    {
        get => _accountPresetSearchText;
        set
        {
            if (SetProperty(ref _accountPresetSearchText, value ?? string.Empty))
            {
                AccountPresetsView.Refresh();
                OnPropertyChanged(nameof(ShowAccountGroupHeaders));
                OnPropertyChanged(nameof(AreAllVisibleAccountPresetsSelected));
            }
        }
    }

    public bool IsEditingAccountPreset => !string.IsNullOrWhiteSpace(_editingAccountPresetId);

    public bool IsAccountPresetEditorOpen
    {
        get => _isAccountPresetEditorOpen;
        private set
        {
            if (SetProperty(ref _isAccountPresetEditorOpen, value))
            {
                RefreshEditorCommandState();
            }
        }
    }

    public string AccountPresetEditorTitle => IsEditingAccountPreset ? "Edit account" : "New account";

    public bool CanSaveAccountPreset => IsAccountPresetEditorOpen && !HasOpenModal && !string.IsNullOrWhiteSpace(AccountPresetEmail);

    public bool IsAccountAdvancedOpen
    {
        get => _isAccountAdvancedOpen;
        set => SetProperty(ref _isAccountAdvancedOpen, value);
    }

    public string AccountPresetName
    {
        get => _accountPresetName;
        set => SetProperty(ref _accountPresetName, value ?? string.Empty);
    }

    public string AccountPresetEmail
    {
        get => _accountPresetEmail;
        set
        {
            if (SetProperty(ref _accountPresetEmail, value ?? string.Empty))
            {
                ((RelayCommand)SaveAccountPresetCommand).RaiseCanExecuteChanged();
                RefreshCurrentCommandState();
            }
        }
    }

    public MerchantKind AccountPresetMerchantHint
    {
        get => _accountPresetMerchantHint;
        set => SetProperty(ref _accountPresetMerchantHint, value);
    }

    public bool AccountPresetIsFavorite
    {
        get => _accountPresetIsFavorite;
        set => SetProperty(ref _accountPresetIsFavorite, value);
    }

    public string AccountPresetNotes
    {
        get => _accountPresetNotes;
        set => SetProperty(ref _accountPresetNotes, value ?? string.Empty);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((RelayCommand)SendDiscordStatsCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsConfirmationOpen
    {
        get => _isConfirmationOpen;
        private set
        {
            if (SetProperty(ref _isConfirmationOpen, value))
            {
                ((RelayCommand)ConfirmDialogCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelDialogCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasOpenModal));
                RefreshEditorCommandState();
            }
        }
    }

    public string ConfirmationTitle
    {
        get => _confirmationTitle;
        private set => SetProperty(ref _confirmationTitle, value ?? string.Empty);
    }

    public string ConfirmationMessage
    {
        get => _confirmationMessage;
        private set => SetProperty(ref _confirmationMessage, value ?? string.Empty);
    }

    public string ConfirmationConfirmText
    {
        get => _confirmationConfirmText;
        private set => SetProperty(ref _confirmationConfirmText, value ?? string.Empty);
    }

    public string ConfirmationCancelText
    {
        get => _confirmationCancelText;
        private set => SetProperty(ref _confirmationCancelText, value ?? string.Empty);
    }

    public bool ConfirmationIsDanger
    {
        get => _confirmationIsDanger;
        private set => SetProperty(ref _confirmationIsDanger, value);
    }

    public bool IsAccountUsageAuditOpen
    {
        get => _isAccountUsageAuditOpen;
        private set
        {
            if (SetProperty(ref _isAccountUsageAuditOpen, value))
            {
                OnPropertyChanged(nameof(HasOpenModal));
                RefreshEditorCommandState();
            }
        }
    }

    public bool HasOpenModal => IsConfirmationOpen || IsAccountUsageAuditOpen;

    public string AccountUsageAuditTitle => _accountUsageAuditPreset is null
        ? "Account usage"
        : $"Orders counted for {_accountUsageAuditPreset.DisplayName}";

    public string AccountUsageAuditSummary
    {
        get
        {
            var activeCount = AccountUsageAuditOrders.Count(order => !order.IsArchived);
            var archivedCount = AccountUsageAuditOrders.Count - activeCount;
            return $"{FormatSimpleCount(AccountUsageAuditOrders.Count, "matching order")} - " +
                   $"{FormatSimpleCount(activeCount, "Orders page result")}, " +
                   $"{FormatSimpleCount(archivedCount, "Archive result")}. Matching uses only the Account/email field.";
        }
    }

    public string DataFilePath => _dataStore.DataFilePath;

    public bool IsDiscordWebhookRevealed
    {
        get => _isDiscordWebhookRevealed;
        set
        {
            if (SetProperty(ref _isDiscordWebhookRevealed, value))
            {
                OnPropertyChanged(nameof(DiscordWebhookRevealLabel));
                OnPropertyChanged(nameof(DiscordWebhookConfigurationStatus));
            }
        }
    }

    public string DiscordWebhookRevealLabel => IsDiscordWebhookRevealed ? "Hide" : "Reveal";

    public string DiscordWebhookConfigurationStatus => string.IsNullOrWhiteSpace(Settings.DiscordWebhookUrl)
        ? "No webhook configured."
        : IsDiscordWebhookRevealed
            ? "Webhook configured and revealed."
            : "Webhook configured and hidden.";

    public string SettingsSaveStatus
    {
        get => _settingsSaveStatus;
        private set => SetProperty(ref _settingsSaveStatus, value);
    }

    public int DashboardMonthRange
    {
        get => Settings.DashboardMonthRange;
        set
        {
            if (Settings.DashboardMonthRange != value)
            {
                Settings.DashboardMonthRange = value;
                OnPropertyChanged();
                RefreshDashboard();
            }
        }
    }

    public bool CanSaveCurrent => !HasOpenModal &&
        (SelectedPage switch
        {
            AppPage.Orders => CanSaveOrder,
            AppPage.Accounts => CanSaveAccountPreset,
            AppPage.Presets => CanSavePreset,
            AppPage.Settings => !Settings.AutoSave,
            _ => false
        });

    public bool CanCloseCurrentPanel => HasOpenModal || GetVisibleEditorPage().HasValue;

    public int MerchantIconCacheVersion
    {
        get => _merchantIconCacheVersion;
        private set => SetProperty(ref _merchantIconCacheVersion, value);
    }

    public string MerchantIconCacheStatus
    {
        get
        {
            var cachedCount = _merchantFaviconService.CachedIconCount;
            if (_isFetchingMerchantFavicons)
            {
                return cachedCount == 0 ? "Fetching merchant icons..." : $"Fetching merchant icons - {cachedCount} cached.";
            }

            return cachedCount == 0 ? "No merchant icons cached." : $"{cachedCount} merchant icon(s) cached.";
        }
    }

    public int LegacyOrderItemMigrationCount => Orders.Count(NeedsLegacyItemMigration);

    public string LegacyOrderItemMigrationStatus => LegacyOrderItemMigrationCount == 0
        ? "Order config is up to date."
        : $"{LegacyOrderItemMigrationCount} order(s) need config update.";

    public void SaveNow(string? message = null)
    {
        _autosaveTimer.Stop();

        try
        {
            _dataStore.Save(_data);
            SettingsSaveStatus = $"Saved {DateTime.Now.ToString("h:mm tt", CultureInfo.CurrentCulture)}";
            if (!string.IsNullOrWhiteSpace(message))
            {
                LastActionMessage = message;
            }
        }
        catch (Exception ex)
        {
            SettingsSaveStatus = "Save failed";
            LastActionMessage = $"Could not save data: {ex.Message}";
        }
    }

    public void CaptureBrowserLinkWindowPlacement()
    {
        _browserLauncher.CaptureTrackedLinkWindowBounds(Settings);
    }

    private void ClearMerchantIconCache()
    {
        var removed = _merchantFaviconService.ClearCache();
        RefreshMerchantIconCacheState(refreshIcons: true);
        LastActionMessage = removed == 0
            ? "Merchant icon cache is already empty."
            : $"Cleared {removed.ToString(CultureInfo.CurrentCulture)} merchant icon cache file(s).";

        if (Settings.FetchMerchantFavicons)
        {
            QueueMerchantFaviconFetch();
        }
    }

    private static bool? GetVisibleSelectionState(System.Collections.IEnumerable items, Func<object, bool> isSelected)
    {
        var visibleItems = items.Cast<object>().ToList();
        if (visibleItems.Count == 0)
        {
            return false;
        }

        var selectedCount = visibleItems.Count(isSelected);
        if (selectedCount == 0)
        {
            return false;
        }

        return selectedCount == visibleItems.Count ? true : null;
    }

    private void QueueMerchantFaviconFetch()
    {
        QueueMerchantFaviconFetch(GetMerchantFaviconCandidates());
    }

    private void QueueMerchantFaviconFetch(MerchantKind merchant)
    {
        QueueMerchantFaviconFetch(new[] { merchant });
    }

    private void QueueMerchantFaviconFetch(IEnumerable<MerchantKind> merchants)
    {
        if (!Settings.FetchMerchantFavicons)
        {
            return;
        }

        var candidates = merchants
            .Where(MerchantFaviconService.CanFetch)
            .Distinct()
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var merchant in candidates)
        {
            _pendingMerchantFaviconFetches.Add(merchant);
        }

        if (!_isFetchingMerchantFavicons)
        {
            _ = FetchPendingMerchantFaviconsAsync();
        }
    }

    private async Task FetchPendingMerchantFaviconsAsync()
    {
        if (_isFetchingMerchantFavicons)
        {
            return;
        }

        _isFetchingMerchantFavicons = true;
        RefreshMerchantIconCacheState();

        try
        {
            while (Settings.FetchMerchantFavicons && _pendingMerchantFaviconFetches.Count > 0)
            {
                var merchants = _pendingMerchantFaviconFetches.ToList();
                _pendingMerchantFaviconFetches.Clear();

                foreach (var merchant in merchants)
                {
                    if (!Settings.FetchMerchantFavicons)
                    {
                        _pendingMerchantFaviconFetches.Clear();
                        break;
                    }

                    var hadCachedIcon = MerchantFaviconService.FindCachedIconPath(merchant) is not null;
                    try
                    {
                        var hasCachedIcon = await _merchantFaviconService.EnsureIconAsync(merchant);
                        if (hasCachedIcon && !hadCachedIcon)
                        {
                            RefreshMerchantIconCacheState(refreshIcons: true);
                            RefreshOrderViews();
                        }
                    }
                    catch
                    {
                        // Individual merchant icon failures are recorded by the cache service.
                    }
                }
            }
        }
        finally
        {
            _isFetchingMerchantFavicons = false;
            RefreshMerchantIconCacheState();
        }
    }

    private IEnumerable<MerchantKind> GetMerchantFaviconCandidates()
    {
        return Orders.Select(order => order.Merchant)
            .Concat(AccountPresets.Select(preset => preset.MerchantHint))
            .Concat(ItemPresets.Select(preset => preset.MerchantHint))
            .Append(Settings.DefaultMerchant)
            .Where(MerchantFaviconService.CanFetch);
    }

    private void RefreshMerchantIconCacheState(bool refreshIcons = false)
    {
        if (refreshIcons)
        {
            MerchantIconCacheVersion++;
        }

        OnPropertyChanged(nameof(MerchantIconCacheStatus));
    }

    private void Navigate(string? page)
    {
        if (Enum.TryParse<AppPage>(page, out var parsed))
        {
            if (parsed == SelectedPage)
            {
                return;
            }

            RequestEditorTransition(
                parsed,
                $"navigate to {GetPageDisplayName(parsed)}",
                () => SelectedPage = parsed);
        }
    }

    private void RequestNewOrder()
    {
        RequestEditorTransition(AppPage.Orders, "open a new order", BeginNewOrder, replaceCurrentEditor: true);
    }

    private void RequestEditOrder(Order? order)
    {
        if (order is null)
        {
            return;
        }

        RequestEditorTransition(AppPage.Orders, "edit another order", () => BeginEditOrder(order), replaceCurrentEditor: true);
    }

    private void MigrateLegacyOrderItems()
    {
        var candidates = Orders.Where(NeedsLegacyItemMigration).ToList();
        if (candidates.Count == 0)
        {
            LastActionMessage = "Order config is already up to date.";
            RefreshLegacyOrderItemMigrationState();
            return;
        }

        var migrated = 0;
        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            foreach (var order in candidates)
            {
                if (order.MigrateLegacyItemFields())
                {
                    migrated++;
                }
            }
        });

        OrdersView.Refresh();
        RefreshDashboard();
        RefreshLegacyOrderItemMigrationState();
        SaveNow($"Updated config for {migrated} order(s).");
    }

    private void BeginNewOrder()
    {
        ResetOrderForm();
        IsOrderEditorOpen = true;
        SelectedPage = AppPage.Orders;
    }

    private void ToggleQuickOrder()
    {
        if (GetVisibleEditorPage() == AppPage.Orders)
        {
            CloseOrderEditor();
            LastActionMessage = "Order panel closed.";
            return;
        }

        RequestNewOrder();
    }

    private void ResetOrderForm()
    {
        _pendingAppliedItemPresets.Clear();
        _editingOrderId = null;
        SelectedOrder = null;
        SelectedOrderAccountPreset = null;
        SelectedOrderPreset = null;
        FormAccountEmail = Settings.DefaultAccountEmail;
        FormMerchant = Settings.DefaultMerchant;
        FormOrderNumber = string.Empty;
        FormOrderLink = string.Empty;
        SetFormItems(new[] { new OrderItem() });
        FormShippingCost = 0m;
        FormTax = 0m;
        FormOtherCost = 0m;
        FormProjectedRoiPercentOverride = null;
        FormProjectedProfitOverride = null;
        FormOrderDate = DateTime.Today;
        FormExpectedDate = null;
        FormDeliveredDate = null;
        FormStatus = OrderStatus.Ordered;
        FormTrackingStatus = string.Empty;
        FormTrackingNumbersText = string.Empty;
        FormNotes = string.Empty;
        IsOrderAdvancedOpen = false;
        OnPropertyChanged(nameof(IsEditingOrder));
        OnPropertyChanged(nameof(OrderEditorTitle));
    }

    private void CloseOrderEditor()
    {
        _pendingAppliedItemPresets.Clear();
        IsOrderEditorOpen = false;
        _editingOrderId = null;
        SelectedOrderAccountPreset = null;
        SelectedOrderPreset = null;
        OnPropertyChanged(nameof(IsEditingOrder));
        OnPropertyChanged(nameof(OrderEditorTitle));
    }

    private void BeginEditOrder(Order? order)
    {
        if (order is null)
        {
            return;
        }

        _pendingAppliedItemPresets.Clear();
        _editingOrderId = order.Id;
        SelectedOrder = order;
        SelectedOrderAccountPreset = null;
        SelectedOrderPreset = null;
        FormAccountEmail = order.AccountEmail;
        FormMerchant = order.Merchant;
        FormOrderNumber = order.OrderNumber;
        FormOrderLink = order.OrderLink;
        SetFormItems(order.Items.Count > 0
            ? order.Items.Select(item => item.Clone())
            : new[] { new OrderItem { Name = order.Item, Quantity = order.Quantity, UnitPrice = order.UnitPrice } });
        FormShippingCost = order.ShippingCost;
        FormTax = order.Tax;
        FormOtherCost = order.OtherCost;
        FormProjectedRoiPercentOverride = order.ProjectedRoiPercentOverride;
        FormProjectedProfitOverride = order.ProjectedProfitOverride;
        FormOrderDate = order.OrderDate;
        FormExpectedDate = order.ExpectedDate;
        FormDeliveredDate = order.DeliveredDate;
        FormStatus = order.Status;
        FormTrackingStatus = order.TrackingStatus;
        FormTrackingNumbersText = string.Join(Environment.NewLine, order.TrackingNumbers.Select(tracking => tracking.Number));
        FormNotes = order.Notes;
        IsOrderAdvancedOpen = order.ShippingCost != 0m || order.ProjectedProfitOverride.HasValue || !string.IsNullOrWhiteSpace(order.TrackingStatus);
        IsOrderEditorOpen = true;
        SelectedPage = AppPage.Orders;
        OnPropertyChanged(nameof(IsEditingOrder));
        OnPropertyChanged(nameof(OrderEditorTitle));
    }

    private void SaveOrder()
    {
        if (!TryApplyOrderInputs(out var items))
        {
            return;
        }

        FormDeliveredDate = OrderState.GetCoherentDeliveredDate(FormStatus, FormDeliveredDate, DateTime.Today);

        var order = Orders.FirstOrDefault(candidate => candidate.Id == _editingOrderId);
        var isNew = order is null;
        order ??= new Order();

        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            UpdateOrderFromForm(order, items);
            CarrierRecognizer.ApplyRecognition(order);
            if (isNew)
            {
                Orders.Add(order);
            }
        });

        CommitPendingItemPresetUsage();
        SelectedOrder = order;
        RefreshAfterOrderChange($"Order {(isNew ? "added" : "updated")}.");
        CloseOrderEditor();
    }

    private void UpdateOrderFromForm(Order order, IEnumerable<OrderItem> items)
    {
        order.AccountEmail = FormAccountEmail.Trim();
        order.Merchant = FormMerchant;
        order.OrderNumber = FormOrderNumber.Trim();
        order.OrderLink = FormOrderLink.Trim();
        order.Items = new ObservableCollection<OrderItem>(items.Select(item => item.Clone()));
        order.ShippingCost = FormShippingCost;
        order.Tax = FormTax;
        order.OtherCost = FormOtherCost;
        order.ProjectedRoiPercentOverride = FormProjectedRoiPercentOverride;
        order.ProjectedProfitOverride = FormProjectedProfitOverride;
        order.OrderDate = FormOrderDate ?? DateTime.Today;
        order.ExpectedDate = FormExpectedDate;
        order.DeliveredDate = FormDeliveredDate;
        order.Status = FormStatus;
        order.TrackingStatus = FormTrackingStatus.Trim();
        order.Notes = FormNotes.Trim();

        order.TrackingNumbers.Clear();
        foreach (var trackingNumber in ParseTrackingNumbers(FormTrackingNumbersText))
        {
            order.TrackingNumbers.Add(new TrackingEntry
            {
                Number = trackingNumber,
                Status = FormTrackingStatus.Trim()
            });
        }
    }

    private bool TryApplyOrderInputs(out List<OrderItem> items)
    {
        items = new List<OrderItem>();

        for (var index = 0; index < FormItems.Count; index++)
        {
            var item = FormItems[index];
            var hasInput = !string.IsNullOrWhiteSpace(item.Name) ||
                           !string.IsNullOrWhiteSpace(item.QuantityInput) ||
                           !string.IsNullOrWhiteSpace(item.UnitPriceInput);

            if (!hasInput)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                LastActionMessage = $"Enter a name for item {index + 1}.";
                return false;
            }

            if (!TryParseQuantity(item.QuantityInput, $"quantity for item {index + 1}", out var itemQuantity) ||
                !TryParseMoney(item.UnitPriceInput, $"unit price for item {index + 1}", out var itemUnitPrice))
            {
                return false;
            }

            item.Quantity = itemQuantity;
            item.UnitPrice = itemUnitPrice;
            items.Add(new OrderItem
            {
                Name = item.Name.Trim(),
                Quantity = itemQuantity,
                UnitPrice = itemUnitPrice
            });
        }

        if (items.Count == 0)
        {
            LastActionMessage = "Add at least one item before saving.";
            return false;
        }

        if (!TryParseMoney(FormShippingCostInput, "shipping", out var shipping) ||
            !TryParseMoney(FormTaxInput, "tax", out var tax) ||
            !TryParseOptionalMoney(FormProjectedProfitInput, "profit", out var projectedProfitOverride) ||
            !TryParseMoney(FormOtherCostInput, "other cost", out var otherCost))
        {
            return false;
        }

        decimal? projectedRoiPercentOverride = null;
        if (!projectedProfitOverride.HasValue &&
            !TryParseOptionalPercent(FormProjectedRoiPercentInput, "ROI", out projectedRoiPercentOverride))
        {
            return false;
        }

        FormShippingCost = shipping;
        FormTax = tax;
        FormOtherCost = otherCost;
        FormProjectedProfitOverride = projectedProfitOverride;
        FormProjectedRoiPercentOverride = projectedProfitOverride.HasValue ? null : projectedRoiPercentOverride;
        return true;
    }

    private void DeleteOrder(Order? order)
    {
        if (order is null)
        {
            return;
        }

        var label = DescribeOrder(order);

        ShowConfirmation(
            order.IsArchived ? "Delete archived order" : "Delete order",
            $"Delete {label}? This permanently removes the order data.",
            "Delete",
            () =>
            {
                RunWithOrderChangeNotificationsSuppressed(() => RemoveOrder(order));
                RefreshAfterOrderChange($"Deleted {label}.");
            },
            isDanger: true,
            cancelMessage: "Delete canceled.");
    }

    private void RemoveOrder(Order order)
    {
        Orders.Remove(order);
        if (SelectedOrder == order)
        {
            SelectedOrder = null;
        }

        if (_editingOrderId == order.Id)
        {
            CloseOrderEditor();
        }
    }

    private static IEnumerable<OrderItem> GetOrderItems(Order order)
    {
        return order.Items.Count > 0
            ? order.Items
            : new[] { new OrderItem { Name = order.Item, Quantity = order.Quantity, UnitPrice = order.UnitPrice } };
    }

    private static bool NeedsLegacyItemMigration(Order order)
    {
        return order.HasLegacyItemFields;
    }

    private void DuplicateOrder(Order? order)
    {
        if (order is null)
        {
            return;
        }

        RequestEditorTransition(
            AppPage.Orders,
            "duplicate the selected order",
            () => DuplicateOrderCore(order),
            replaceCurrentEditor: IsOrderEditorOpen);
    }

    private void DuplicateOrderCore(Order order)
    {

        var copy = new Order
        {
            AccountEmail = order.AccountEmail,
            Merchant = order.Merchant,
            Items = new ObservableCollection<OrderItem>(GetOrderItems(order).Select(item => item.Clone())),
            ShippingCost = order.ShippingCost,
            Tax = order.Tax,
            OtherCost = order.OtherCost,
            ProjectedRoiPercentOverride = order.ProjectedRoiPercentOverride,
            ProjectedProfitOverride = order.ProjectedProfitOverride,
            OrderDate = DateTime.Today,
            ExpectedDate = null,
            DeliveredDate = null,
            Status = OrderStatus.Ordered,
            Notes = string.IsNullOrWhiteSpace(order.OrderNumber)
                ? "Duplicated order."
                : $"Duplicated from {order.OrderNumber.Trim()}."
        };

        RunWithOrderChangeNotificationsSuppressed(() => Orders.Add(copy));
        BeginEditOrder(copy);
        RefreshAfterOrderChange("Duplicated order. Add the new order number and tracking when ready.");
    }

    private void ToggleCompleted(Order? order)
    {
        if (order is null || !order.CanToggleDelivered)
        {
            return;
        }

        var message = "Order updated.";
        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            if (order.CanArchive)
            {
                order.Status = order.TrackingNumbers.Count > 0 ? OrderStatus.Shipped : OrderStatus.Ordered;
                order.DeliveredDate = null;
                message = "Marked order as not completed.";
            }
            else
            {
                order.Status = OrderStatus.Delivered;
                order.DeliveredDate = DateTime.Today;
                message = "Marked order as completed.";
            }
        });

        if (_editingOrderId == order.Id)
        {
            BeginEditOrder(order);
        }

        RefreshAfterOrderChange(message);
    }

    private void ArchiveCompletedOrders()
    {
        var candidates = Orders
            .Where(order => order.CanArchive && !order.IsArchived)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = "No completed orders are ready to archive.";
            RefreshArchiveState();
            return;
        }

        var noun = candidates.Count == 1 ? "order" : "orders";
        ShowConfirmation(
            "Archive completed orders",
            $"Archive {candidates.Count} completed {noun}? Archived orders move out of the Orders list and can be restored from the Archive page.{FormatCandidateExamples(candidates, DescribeOrder)}",
            "Archive",
            () => ArchiveOrders(candidates, $"Archived {candidates.Count} completed {noun}."),
            cancelMessage: "Archive canceled.");
    }

    private void ArchiveOrders(IReadOnlyCollection<Order> candidates, string message)
    {
        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            foreach (var order in candidates)
            {
                order.IsArchived = true;
                order.IsSelected = false;
            }
        });

        var editingOrder = Orders.FirstOrDefault(order => order.Id == _editingOrderId);
        if (SelectedOrder?.IsArchived == true)
        {
            SelectedOrder = null;
        }

        if (editingOrder?.IsArchived == true)
        {
            CloseOrderEditor();
        }

        RefreshAfterOrderChange(message);
    }

    private void RestoreOrder(Order? order)
    {
        if (order is null)
        {
            return;
        }

        var label = DescribeOrder(order);
        var message = HideCompleted && order.IsFinal
            ? $"Restored {label}. Turn off Hide completed to view it in Orders."
            : $"Restored {label} to Orders.";

        ShowConfirmation(
            "Restore archived order",
            $"Restore {label} to Orders? It will leave the Archive page and return to the active Orders list.",
            "Restore",
            () => RestoreOrders(new[] { order }, message, selectedOrder: order),
            cancelMessage: "Restore canceled.");
    }

    private void RestoreOrders(IReadOnlyCollection<Order> candidates, string message, Order? selectedOrder = null)
    {
        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            foreach (var order in candidates)
            {
                order.IsArchived = false;
                order.IsSelected = false;
            }
        });

        SelectedPage = AppPage.Orders;
        if (selectedOrder is not null)
        {
            SelectedOrder = selectedOrder;
        }

        RefreshAfterOrderChange(message);
    }

    private void RunPrimaryOrderAction(Order? order)
    {
        if (order is null || order.IsArchived)
        {
            return;
        }

        if (order.CanMarkDelivered)
        {
            ToggleCompleted(order);
            return;
        }

        if (!order.CanArchive)
        {
            return;
        }

        var label = DescribeOrder(order);
        ShowConfirmation(
            "Archive delivered order",
            $"Archive {label}? It will move out of Orders and remain available on the Archive page.",
            "Archive",
            () => ArchiveOrders(new[] { order }, $"Archived {label}."),
            cancelMessage: "Archive canceled.");
    }

    private void SelectOrders(System.Collections.IEnumerable orders, bool selected)
    {
        RunWithBulkSelectionNotificationsSuppressed(() =>
        {
            foreach (var value in orders)
            {
                if (value is Order order)
                {
                    order.IsSelected = selected;
                }
            }
        });
    }

    private void ApplyOrderAttentionFilter(object? parameter)
    {
        var filter = parameter switch
        {
            OrderAttentionFilter typedFilter => typedFilter,
            string text when Enum.TryParse<OrderAttentionFilter>(text, out var parsed) => parsed,
            _ => OrderAttentionFilter.All
        };

        RequestEditorTransition(
            AppPage.Orders,
            "show the requested Orders alert",
            () =>
            {
                SearchText = string.Empty;
                HideCompleted = false;
                SelectedAttentionFilter = filter;
                SelectedPage = AppPage.Orders;
                LastActionMessage = AttentionFilterSummary;
            });
    }

    private void ToggleVisibleOrderItemsExpansion()
    {
        var orders = GetVisibleOrdersWithItems().ToList();
        if (orders.Count == 0)
        {
            LastActionMessage = "No visible order items to expand.";
            RefreshItemExpansionToggleState();
            return;
        }

        var expand = orders.Any(order => !order.IsItemsExpanded);
        foreach (var order in orders)
        {
            order.IsItemsExpanded = expand;
        }

        var noun = orders.Count == 1 ? "order" : "orders";
        LastActionMessage = expand
            ? $"Expanded item details for {orders.Count} visible {noun}."
            : $"Collapsed item details for {orders.Count} visible {noun}.";
        RefreshItemExpansionToggleState();
    }

    private void ClearOrderSelection(bool includeArchived)
    {
        RunWithBulkSelectionNotificationsSuppressed(() =>
        {
            foreach (var order in Orders.Where(order => order.IsArchived == includeArchived && order.IsSelected))
            {
                order.IsSelected = false;
            }
        });
    }

    private void MarkSelectedOrdersCompleted()
    {
        var candidates = Orders
            .Where(order => order.IsSelected && !order.IsArchived && order.CanMarkDelivered)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = "No selected active orders need completion.";
            RefreshBulkSelectionState();
            return;
        }

        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            foreach (var order in candidates)
            {
                order.Status = OrderStatus.Delivered;
                order.DeliveredDate ??= DateTime.Today;
                order.IsSelected = false;
            }
        });

        if (_editingOrderId is not null && candidates.Any(order => order.Id == _editingOrderId))
        {
            var editedOrder = Orders.FirstOrDefault(order => order.Id == _editingOrderId);
            if (editedOrder is not null)
            {
                BeginEditOrder(editedOrder);
            }
        }

        var noun = candidates.Count == 1 ? "order" : "orders";
        RefreshAfterOrderChange($"Marked {candidates.Count} selected {noun} completed.");
    }

    private void ArchiveSelectedCompletedOrders()
    {
        var candidates = Orders
            .Where(order => order.IsSelected && !order.IsArchived && order.CanArchive)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = "Select completed orders before archiving.";
            RefreshBulkSelectionState();
            return;
        }

        var noun = candidates.Count == 1 ? "order" : "orders";
        ShowConfirmation(
            "Archive selected orders",
            $"Archive {candidates.Count} selected completed {noun}? Archived orders move out of the Orders list and can be restored from the Archive page.{FormatCandidateExamples(candidates, DescribeOrder)}",
            "Archive",
            () => ArchiveOrders(candidates, $"Archived {candidates.Count} selected {noun}."),
            cancelMessage: "Archive canceled.");
    }

    private void RestoreSelectedOrders()
    {
        var candidates = Orders
            .Where(order => order.IsSelected && order.IsArchived)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = "No archived orders are selected.";
            RefreshBulkSelectionState();
            return;
        }

        var noun = candidates.Count == 1 ? "order" : "orders";
        var message = HideCompleted && candidates.Any(order => order.IsFinal)
            ? $"Restored {candidates.Count} {noun}. Turn off Hide completed to view final orders."
            : $"Restored {candidates.Count} {noun} to Orders.";

        ShowConfirmation(
            "Restore selected orders",
            $"Restore {candidates.Count} selected archived {noun} to Orders? They will leave the Archive page and return to the active Orders list.{FormatCandidateExamples(candidates, DescribeOrder)}",
            "Restore",
            () => RestoreOrders(candidates, message),
            cancelMessage: "Restore canceled.");
    }

    private void DeleteSelectedOrders(bool includeArchived)
    {
        var candidates = Orders
            .Where(order => order.IsSelected && order.IsArchived == includeArchived)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = includeArchived
                ? "No archived orders are selected."
                : "No active orders are selected.";
            RefreshBulkSelectionState();
            return;
        }

        var noun = candidates.Count == 1 ? "order" : "orders";
        ShowConfirmation(
            includeArchived ? "Delete archived orders" : "Delete selected orders",
            $"Delete {candidates.Count} selected {noun}? This permanently removes the selected order data.{FormatCandidateExamples(candidates, DescribeOrder)}",
            "Delete",
            () =>
            {
                RunWithOrderChangeNotificationsSuppressed(() =>
                {
                    foreach (var order in candidates)
                    {
                        RemoveOrder(order);
                    }
                });

                RefreshAfterOrderChange($"Deleted {candidates.Count} selected {noun}.");
            },
            isDanger: true,
            cancelMessage: "Delete canceled.");
    }

    private void OpenOrderLink(Order? order)
    {
        if (order is null)
        {
            return;
        }

        var recognitionChanged = ApplyRecognitionQuietly(order);
        var url = CarrierRecognizer.BuildOrderUrl(order);
        LastActionMessage = "Opening order link...";
        _ = OpenUrlAndRefreshOrdersAsync(url, BuildBrowserSessionContext(order, url), recognitionChanged);
    }

    private void OpenTracking(TrackingEntry? tracking)
    {
        if (tracking is null)
        {
            return;
        }

        var order = Orders.FirstOrDefault(candidate => candidate.TrackingNumbers.Contains(tracking));
        if (order is null)
        {
            LastActionMessage = "Tracking number is not attached to an order.";
            return;
        }

        var recognitionChanged = ApplyRecognitionQuietly(order);
        var url = CarrierRecognizer.BuildTrackingUrl(order, tracking);
        LastActionMessage = "Opening tracking link...";
        _ = OpenUrlAndRefreshOrdersAsync(url, BuildBrowserSessionContext(order, url), recognitionChanged);
    }

    private async Task OpenUrlAndRefreshOrdersAsync(string url, BrowserSessionContext? sessionContext, bool refreshAfterOpen)
    {
        try
        {
            LastActionMessage = await OpenUrlAsync(url, sessionContext);
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Could not open link: {ex.Message}";
        }
        finally
        {
            if (refreshAfterOpen)
            {
                RefreshAfterOrderChange();
            }
        }
    }

    private void SaveCurrent()
    {
        if (HasOpenModal)
        {
            return;
        }

        switch (SelectedPage)
        {
            case AppPage.Orders when CanSaveOrder:
                SaveOrder();
                break;
            case AppPage.Accounts when CanSaveAccountPreset:
                SaveAccountPreset();
                break;
            case AppPage.Presets when CanSavePreset:
                SavePreset();
                break;
            case AppPage.Settings when !Settings.AutoSave:
                SaveNow("Settings saved.");
                break;
        }
    }

    private void CloseCurrentPanel()
    {
        if (IsAccountUsageAuditOpen)
        {
            CloseAccountUsageAudit();
            return;
        }

        if (IsConfirmationOpen)
        {
            CancelDialog();
            return;
        }

        switch (GetVisibleEditorPage())
        {
            case AppPage.Orders:
                CloseOrderEditor();
                break;
            case AppPage.Accounts:
                CloseAccountPresetEditor();
                break;
            case AppPage.Presets:
                ClosePresetEditor();
                break;
        }
    }

    private void RequestEditorTransition(
        AppPage targetPage,
        string requestedAction,
        Action transition,
        bool replaceCurrentEditor = false)
    {
        if (HasOpenModal)
        {
            return;
        }

        var openEditorPages = GetOpenEditorPages();
        var requiresDiscard = openEditorPages.Any(page => page != targetPage) ||
                              replaceCurrentEditor && openEditorPages.Contains(targetPage);
        if (!requiresDiscard)
        {
            transition();
            return;
        }

        var editorName = GetPageDisplayName(GetVisibleEditorPage() ?? openEditorPages[0]);
        ShowConfirmation(
            "Discard open editor?",
            $"Close the open {editorName} editor and {requestedAction}? Unsaved editor changes will be lost.",
            "Discard and continue",
            () =>
            {
                CloseAllEditors();
                transition();
            },
            cancelMessage: $"Kept the open {editorName} editor.");
    }

    private List<AppPage> GetOpenEditorPages()
    {
        var pages = new List<AppPage>(3);
        if (IsOrderEditorOpen)
        {
            pages.Add(AppPage.Orders);
        }

        if (IsAccountPresetEditorOpen)
        {
            pages.Add(AppPage.Accounts);
        }

        if (IsPresetEditorOpen)
        {
            pages.Add(AppPage.Presets);
        }

        return pages;
    }

    private AppPage? GetVisibleEditorPage()
    {
        return SelectedPage switch
        {
            AppPage.Orders when IsOrderEditorOpen => AppPage.Orders,
            AppPage.Accounts when IsAccountPresetEditorOpen => AppPage.Accounts,
            AppPage.Presets when IsPresetEditorOpen => AppPage.Presets,
            _ => null
        };
    }

    private void CloseEditorsExcept(AppPage page)
    {
        if (page != AppPage.Orders && IsOrderEditorOpen)
        {
            CloseOrderEditor();
        }

        if (page != AppPage.Accounts && IsAccountPresetEditorOpen)
        {
            CloseAccountPresetEditor();
        }

        if (page != AppPage.Presets && IsPresetEditorOpen)
        {
            ClosePresetEditor();
        }
    }

    private void CloseAllEditors()
    {
        if (IsOrderEditorOpen)
        {
            CloseOrderEditor();
        }

        if (IsAccountPresetEditorOpen)
        {
            CloseAccountPresetEditor();
        }

        if (IsPresetEditorOpen)
        {
            ClosePresetEditor();
        }
    }

    private static string GetPageDisplayName(AppPage page)
    {
        return page switch
        {
            AppPage.Presets => "Items",
            _ => page.ToString()
        };
    }

    private void ClearDiscordWebhook()
    {
        ShowConfirmation(
            "Clear Discord webhook",
            "Remove the configured Discord webhook URL from this app? Discord stats will remain disabled until another URL is entered.",
            "Clear webhook",
            () =>
            {
                Settings.DiscordWebhookUrl = string.Empty;
                Settings.DiscordEnabled = false;
                IsDiscordWebhookRevealed = false;
                SaveNow("Discord webhook cleared.");
            },
            isDanger: true,
            cancelMessage: "Webhook clear canceled.");
    }

    private async Task<string> OpenUrlAsync(string url, BrowserSessionContext? sessionContext)
    {
        try
        {
            return await Task.Run(() => _browserLauncher.OpenUrl(url, Settings, sessionContext));
        }
        catch (Exception ex)
        {
            return $"Could not open link: {ex.Message}";
        }
    }

    private BrowserSessionContext? BuildBrowserSessionContext(Order order, string url)
    {
        if (!Settings.UseAccountBrowserSessions ||
            string.IsNullOrWhiteSpace(order.AccountEmail) ||
            !IsAccountSessionUrl(order.Merchant, url))
        {
            return null;
        }

        var accountEmail = order.AccountEmail.Trim();
        var preset = AccountPresets.FirstOrDefault(candidate =>
            candidate.MerchantHint == order.Merchant &&
            string.Equals(candidate.Email.Trim(), accountEmail, StringComparison.OrdinalIgnoreCase)) ??
            AccountPresets.FirstOrDefault(candidate =>
                string.Equals(candidate.Email.Trim(), accountEmail, StringComparison.OrdinalIgnoreCase));

        return new BrowserSessionContext
        {
            Merchant = order.Merchant,
            AccountKey = accountEmail,
            AccountDisplayName = preset?.DisplayName ?? accountEmail
        };
    }

    private BrowserSessionContext? BuildBrowserSessionContext(AccountPreset preset, MerchantKind merchant, string url)
    {
        if (!Settings.UseAccountBrowserSessions ||
            !IsAccountSessionUrl(merchant, url))
        {
            return null;
        }

        return BuildBrowserSessionContext(preset, merchant);
    }

    private static BrowserSessionContext? BuildBrowserSessionContext(AccountPreset preset, MerchantKind merchant)
    {
        if (string.IsNullOrWhiteSpace(preset.Email))
        {
            return null;
        }

        return new BrowserSessionContext
        {
            Merchant = merchant,
            AccountKey = preset.Email.Trim(),
            AccountDisplayName = preset.DisplayName
        };
    }

    private static bool IsAccountSessionUrl(MerchantKind merchant, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return merchant switch
        {
            MerchantKind.Amazon => IsHostOrSubdomain(uri.Host, "amazon.com"),
            MerchantKind.Target => IsHostOrSubdomain(uri.Host, "target.com"),
            _ => false
        };
    }

    private static bool IsHostOrSubdomain(string host, string domain)
    {
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);
    }

    private bool ApplyRecognitionQuietly(Order order)
    {
        var changed = false;
        RunWithOrderChangeNotificationsSuppressed(() => changed = CarrierRecognizer.ApplyRecognition(order));
        return changed;
    }

    private void RunWithOrderChangeNotificationsSuppressed(Action action)
    {
        var previousSuppression = _suppressOrderChangeNotifications;
        _suppressOrderChangeNotifications = true;
        try
        {
            action();
        }
        finally
        {
            _suppressOrderChangeNotifications = previousSuppression;
        }
    }

    private void RunWithPresetChangeNotificationsSuppressed(Action action)
    {
        var previousSuppression = _suppressPresetChangeNotifications;
        _suppressPresetChangeNotifications = true;
        try
        {
            action();
        }
        finally
        {
            _suppressPresetChangeNotifications = previousSuppression;
        }

        if (!previousSuppression && !_isFlushingPresetRefreshes)
        {
            FlushPendingPresetRefreshes();
        }
    }

    private void RunWithBulkSelectionNotificationsSuppressed(Action action)
    {
        var previousSuppression = _suppressBulkSelectionNotifications;
        _suppressBulkSelectionNotifications = true;
        try
        {
            action();
        }
        finally
        {
            _suppressBulkSelectionNotifications = previousSuppression;
        }

        if (!previousSuppression)
        {
            RefreshBulkSelectionState();
        }
    }

    private static IEnumerable<string> ParseTrackingNumbers(string text)
    {
        return (text ?? string.Empty)
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CarrierRecognizer.NormalizeTrackingNumber)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshOrderPreview()
    {
        OnPropertyChanged(nameof(FormSubtotal));
        OnPropertyChanged(nameof(FormTotal));
        OnPropertyChanged(nameof(FormEffectiveRoiPercent));
        OnPropertyChanged(nameof(FormProjectedProfit));
        OnPropertyChanged(nameof(FormProjectionSummary));
        OnPropertyChanged(nameof(HasFormCostDetails));
        OnPropertyChanged(nameof(FormCostDetailSummary));
    }

    private static int ParseQuantityPreview(string input)
    {
        return int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out var current) ||
               int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out current)
            ? Math.Max(1, current)
            : 1;
    }

    private static decimal ParseMoneyPreview(string input)
    {
        return TryParseMoneyValue(input, out var value)
            ? Math.Max(0m, value)
            : 0m;
    }

    private static decimal? ParseOptionalMoneyPreview(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        return TryParseMoneyValue(input, out var value) && value >= 0m ? value : null;
    }

    private static decimal? ParseOptionalPercentPreview(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim().TrimEnd('%');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var current) ||
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out current)
            ? Math.Max(0m, current)
            : null;
    }

    private bool TryParseQuantity(string input, string label, out int value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = 1;
            return true;
        }

        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) ||
            int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            value = Math.Max(1, value);
            return true;
        }

        LastActionMessage = $"Enter a valid {label}.";
        value = 1;
        return false;
    }

    private bool TryParseMoney(string input, string label, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = 0m;
            return true;
        }

        if (TryParseMoneyValue(input, out value))
        {
            if (value < 0m)
            {
                LastActionMessage = $"Enter a non-negative {label}.";
                value = 0m;
                return false;
            }

            return true;
        }

        LastActionMessage = $"Enter a valid {label}.";
        value = 0m;
        return false;
    }

    private bool TryParseOptionalMoney(string input, string label, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (TryParseMoneyValue(input, out var currentValue))
        {
            if (currentValue < 0m)
            {
                LastActionMessage = $"Enter a non-negative {label}.";
                value = null;
                return false;
            }

            value = currentValue;
            return true;
        }

        LastActionMessage = $"Enter a valid {label}.";
        value = null;
        return false;
    }

    private static bool TryParseMoneyValue(string input, out decimal value)
    {
        return decimal.TryParse(input, NumberStyles.Currency, CultureInfo.CurrentCulture, out value) ||
               decimal.TryParse(input, NumberStyles.Currency, CultureInfo.InvariantCulture, out value);
    }

    private bool TryParseOptionalPercent(string input, string label, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        var text = input.Trim();
        if (text.EndsWith("%", StringComparison.Ordinal))
        {
            text = text[..^1].Trim();
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentValue) ||
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out currentValue))
        {
            value = Math.Max(0m, currentValue);
            return true;
        }

        LastActionMessage = $"Enter a valid {label}.";
        value = null;
        return false;
    }

    private static string FormatQuantityInput(int value)
    {
        return value <= 1 ? string.Empty : value.ToString(CultureInfo.CurrentCulture);
    }

    private static string FormatMoneyInput(decimal value)
    {
        return value == 0m ? string.Empty : value.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static string FormatOptionalMoneyInput(decimal? value)
    {
        return value.HasValue
            ? Math.Max(0m, value.Value).ToString("0.00", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    private static string FormatPercentInput(decimal? value)
    {
        return value.HasValue
            ? Math.Max(0m, value.Value).ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    public void CopyTrackingNumbers(Order? order)
    {
        if (order is null)
        {
            return;
        }

        CopyTrackingNumbers(new[] { order });
    }

    public void CopyTrackingNumbers(IEnumerable<Order> orders)
    {
        var orderList = orders
            .Where(order => order is not null)
            .Distinct()
            .ToList();

        var text = string.Join(
            Environment.NewLine,
            orderList.SelectMany(order => order.TrackingNumbers)
                .Select(tracking => tracking.Number)
                .Where(number => !string.IsNullOrWhiteSpace(number)));

        if (string.IsNullOrWhiteSpace(text))
        {
            LastActionMessage = orderList.Count == 1
                ? "That order has no tracking numbers to copy."
                : "The selected orders have no tracking numbers to copy.";
            return;
        }

        try
        {
            Clipboard.SetText(text);
            LastActionMessage = orderList.Count == 1
                ? "Tracking numbers copied to clipboard."
                : $"Tracking numbers copied from {orderList.Count} orders.";
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Could not copy tracking numbers: {ex.Message}";
        }
    }

    public void CopyText(object? parameter)
    {
        var text = ExtractClipboardText(parameter);
        if (string.IsNullOrWhiteSpace(text))
        {
            LastActionMessage = "No value is available to copy.";
            return;
        }

        try
        {
            Clipboard.SetText(text);
            LastActionMessage = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Could not copy value: {ex.Message}";
        }
    }

    private static bool HasTextToCopy(object? parameter)
    {
        return !string.IsNullOrWhiteSpace(ExtractClipboardText(parameter));
    }

    private static string ExtractClipboardText(object? parameter)
    {
        return parameter switch
        {
            string text => text.Trim(),
            Order order => order.AccountEmail.Trim(),
            AccountPreset preset => preset.Email.Trim(),
            TrackingEntry tracking => tracking.Number.Trim(),
            _ => string.Empty
        };
    }

    private void AddFormItem()
    {
        AddFormItem(new OrderItem());
    }

    private void AddFormItem(OrderItem item)
    {
        item.RefreshInputs();
        FormItems.Add(item);
    }

    private void RemoveFormItem(OrderItem? item)
    {
        if (item is null || FormItems.Count <= 1)
        {
            return;
        }

        FormItems.Remove(item);
    }

    private void SetFormItems(IEnumerable<OrderItem> items)
    {
        FormItems.Clear();
        foreach (var item in items)
        {
            AddFormItem(item);
        }

        if (FormItems.Count == 0)
        {
            AddFormItem();
        }
    }

    private void FormItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OrderItem item in e.OldItems)
            {
                item.PropertyChanged -= FormItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OrderItem item in e.NewItems)
            {
                item.PropertyChanged += FormItemPropertyChanged;
            }
        }

        ((RelayCommand)SaveOrderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveOrderItemCommand).RaiseCanExecuteChanged();
        RefreshOrderPreview();
    }

    private void FormItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderItem.Name))
        {
            ((RelayCommand)SaveOrderCommand).RaiseCanExecuteChanged();
            RefreshCurrentCommandState();
        }

        if (e.PropertyName is nameof(OrderItem.QuantityInput) or nameof(OrderItem.UnitPriceInput) or nameof(OrderItem.Quantity) or nameof(OrderItem.UnitPrice))
        {
            RefreshOrderPreview();
        }
    }

    private void ApplyAccountPreset(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (GetVisibleEditorPage() == AppPage.Orders)
        {
            ApplyAccountPresetToOrder(preset);
            return;
        }

        RequestEditorTransition(
            AppPage.Orders,
            "use the account in a new order",
            () =>
            {
                BeginNewOrder();
                ApplyAccountPresetToOrder(preset);
            });
    }

    private void ApplyAccountPresetToOrder(AccountPreset preset)
    {
        FormAccountEmail = preset.Email;
        if (preset.MerchantHint != MerchantKind.Unknown)
        {
            FormMerchant = preset.MerchantHint;
        }

        SelectedPage = AppPage.Orders;
        LastActionMessage = $"Applied account '{preset.DisplayName}'.";
        PersistIfNeeded();
    }

    private async Task ViewAccountOrdersAsync(AccountPreset? preset)
    {
        if (preset is null ||
            !CarrierRecognizer.TryBuildOrderHistoryUrl(preset.MerchantHint, out var url))
        {
            LastActionMessage = preset is null
                ? "Select an account before viewing order history."
                : $"Account order history is not available for {EnumDisplayFormatter.Format(preset.MerchantHint)}.";
            return;
        }

        LastActionMessage = "Opening account orders...";
        LastActionMessage = await OpenUrlAsync(url, BuildBrowserSessionContext(preset, preset.MerchantHint, url));
    }

    private static bool CanViewAccountOrders(AccountPreset? preset)
    {
        return preset is { SupportsOrderHistory: true };
    }

    private void ClearAccountSession(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (!preset.SupportsIsolatedBrowserSession)
        {
            LastActionMessage = $"Browser sessions are not available for {EnumDisplayFormatter.Format(preset.MerchantHint)}.";
            return;
        }

        if (!Settings.UseAccountBrowserSessions)
        {
            LastActionMessage = "Account browser sessions are turned off in Settings.";
            return;
        }

        if (string.IsNullOrWhiteSpace(preset.Email))
        {
            LastActionMessage = "Add an email to the account before clearing its browser session.";
            return;
        }

        var merchant = preset.MerchantHint;
        var sessionContext = BuildBrowserSessionContext(preset, merchant);
        if (sessionContext is null)
        {
            LastActionMessage = $"Browser sessions are not available for {merchant}.";
            return;
        }

        var label = DescribeAccountPreset(preset);
        ShowConfirmation(
            "Clear account session",
            $"Clear the {merchant} browser session for {label}? This removes cookies, sign-in state, and cached browser data for this account. A clean session folder will be created immediately.",
            "Clear session",
            () => LastActionMessage = _browserLauncher.ClearAccountSession(sessionContext),
            isDanger: true,
            cancelMessage: "Clear session canceled.");
    }

    private bool CanClearAccountSession(AccountPreset? preset)
    {
        return Settings.UseAccountBrowserSessions &&
            preset is not null &&
            preset.SupportsIsolatedBrowserSession &&
            !string.IsNullOrWhiteSpace(preset.Email);
    }

    private void ApplyPreset(ItemPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (GetVisibleEditorPage() == AppPage.Orders)
        {
            ApplyPresetToOrder(preset);
            return;
        }

        RequestEditorTransition(
            AppPage.Orders,
            "use the item in a new order",
            () =>
            {
                BeginNewOrder();
                ApplyPresetToOrder(preset);
            });
    }

    private void ApplyPresetToOrder(ItemPreset preset)
    {
        var target = FormItems.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Name));

        if (target is null)
        {
            target = new OrderItem();
            AddFormItem(target);
        }

        target.Name = preset.Name;
        target.Quantity = preset.DefaultQuantity;
        target.UnitPrice = preset.DefaultUnitPrice;
        FormShippingCost = PresetWorkflowRules.ApplyMoneyDefault(ParseMoneyPreview(FormShippingCostInput), preset.DefaultShipping);
        FormTax = PresetWorkflowRules.ApplyMoneyDefault(ParseMoneyPreview(FormTaxInput), preset.DefaultTax);

        if (preset.MerchantHint != MerchantKind.Unknown)
        {
            FormMerchant = preset.MerchantHint;
        }

        _pendingAppliedItemPresets.Add(preset);

        SelectedPage = AppPage.Orders;
        LastActionMessage = $"Applied item '{preset.Name}'.";
        PersistIfNeeded();
    }

    private void CommitPendingItemPresetUsage()
    {
        if (_pendingAppliedItemPresets.Count == 0)
        {
            return;
        }

        var appliedPresets = _pendingAppliedItemPresets
            .Where(ItemPresets.Contains)
            .ToList();
        _pendingAppliedItemPresets.Clear();
        if (appliedPresets.Count == 0)
        {
            return;
        }

        RunWithPresetChangeNotificationsSuppressed(() =>
        {
            foreach (var preset in appliedPresets)
            {
                preset.UsageCount++;
            }
        });
    }

    private void BeginNewAccountPreset()
    {
        ResetAccountPresetForm();
        IsAccountPresetEditorOpen = true;
        SelectedPage = AppPage.Accounts;
    }

    private void RequestNewAccountPreset()
    {
        RequestEditorTransition(AppPage.Accounts, "open a new account", BeginNewAccountPreset, replaceCurrentEditor: true);
    }

    private void RequestEditAccountPreset(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        RequestEditorTransition(AppPage.Accounts, "edit another account", () => BeginEditAccountPreset(preset), replaceCurrentEditor: true);
    }

    private void ToggleQuickAccountPreset()
    {
        if (GetVisibleEditorPage() == AppPage.Accounts)
        {
            CloseAccountPresetEditor();
            LastActionMessage = "Account panel closed.";
            return;
        }

        RequestNewAccountPreset();
    }

    private void ResetAccountPresetForm()
    {
        _editingAccountPresetId = null;
        SelectedAccountPreset = null;
        AccountPresetName = string.Empty;
        AccountPresetEmail = string.Empty;
        AccountPresetMerchantHint = MerchantKind.Unknown;
        AccountPresetIsFavorite = false;
        AccountPresetNotes = string.Empty;
        IsAccountAdvancedOpen = false;
        OnPropertyChanged(nameof(IsEditingAccountPreset));
        OnPropertyChanged(nameof(AccountPresetEditorTitle));
    }

    private void CloseAccountPresetEditor()
    {
        IsAccountPresetEditorOpen = false;
        _editingAccountPresetId = null;
        OnPropertyChanged(nameof(IsEditingAccountPreset));
        OnPropertyChanged(nameof(AccountPresetEditorTitle));
    }

    private void BeginEditAccountPreset(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        _editingAccountPresetId = preset.Id;
        SelectedAccountPreset = preset;
        AccountPresetName = preset.Name;
        AccountPresetEmail = preset.Email;
        AccountPresetMerchantHint = preset.MerchantHint;
        AccountPresetIsFavorite = preset.IsFavorite;
        AccountPresetNotes = preset.Notes;
        IsAccountAdvancedOpen = preset.IsFavorite || !string.IsNullOrWhiteSpace(preset.Notes);
        IsAccountPresetEditorOpen = true;
        SelectedPage = AppPage.Accounts;
        OnPropertyChanged(nameof(IsEditingAccountPreset));
        OnPropertyChanged(nameof(AccountPresetEditorTitle));
    }

    private void SaveAccountPreset()
    {
        var email = AccountPresetEmail.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            LastActionMessage = "Enter an account email before saving.";
            return;
        }

        var preset = AccountPresets.FirstOrDefault(candidate => candidate.Id == _editingAccountPresetId);
        var isNew = preset is null;
        preset ??= new AccountPreset();

        RunWithPresetChangeNotificationsSuppressed(() =>
        {
            preset.Name = AccountPresetName.Trim();
            preset.Email = email;
            preset.MerchantHint = AccountPresetMerchantHint;
            preset.IsFavorite = AccountPresetIsFavorite;
            preset.Notes = AccountPresetNotes.Trim();
            if (isNew)
            {
                preset.CreatedAt = DateTime.Now;
                AccountPresets.Add(preset);
            }
        });

        SelectedAccountPreset = preset;
        RaiseAccountActionCommandState();
        SaveNow($"Account preset {(isNew ? "added" : "updated")}.");
        CloseAccountPresetEditor();
    }

    private void DeleteAccountPreset(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        var label = DescribeAccountPreset(preset);

        ShowConfirmation(
            "Delete account preset",
            $"Delete {label}? This permanently removes the saved account shortcut.",
            "Delete",
            () =>
            {
                RunWithPresetChangeNotificationsSuppressed(() => AccountPresets.Remove(preset));
                if (SelectedAccountPreset == preset)
                {
                    SelectedAccountPreset = null;
                }

                if (_editingAccountPresetId == preset.Id)
                {
                    CloseAccountPresetEditor();
                }

                SaveNow($"Deleted {label}.");
            },
            isDanger: true,
            cancelMessage: "Delete canceled.");
    }

    private void DuplicateAccountPreset(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        RequestEditorTransition(
            AppPage.Accounts,
            "duplicate the selected account",
            () => DuplicateAccountPresetCore(preset),
            replaceCurrentEditor: IsAccountPresetEditorOpen);
    }

    private void DuplicateAccountPresetCore(AccountPreset preset)
    {

        var copy = new AccountPreset
        {
            CreatedAt = DateTime.Now,
            Name = $"{preset.DisplayName} copy".Trim(),
            Email = preset.Email,
            MerchantHint = preset.MerchantHint,
            IsFavorite = preset.IsFavorite,
            Notes = preset.Notes
        };

        RunWithPresetChangeNotificationsSuppressed(() => AccountPresets.Add(copy));
        SelectedAccountPreset = copy;
        SaveNow("Account preset duplicated.");
        BeginEditAccountPreset(copy);
    }

    private void SelectAccountPresets(System.Collections.IEnumerable presets, bool selected)
    {
        RunWithBulkSelectionNotificationsSuppressed(() =>
        {
            foreach (var value in presets)
            {
                if (value is AccountPreset preset)
                {
                    preset.IsSelected = selected;
                }
            }
        });
    }

    private void ClearAccountPresetSelection()
    {
        RunWithBulkSelectionNotificationsSuppressed(() =>
        {
            foreach (var preset in AccountPresets.Where(preset => preset.IsSelected))
            {
                preset.IsSelected = false;
            }
        });
    }

    private void DeleteSelectedAccountPresets()
    {
        var candidates = AccountPresets
            .Where(preset => preset.IsSelected)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = "No accounts are selected.";
            RefreshBulkSelectionState();
            return;
        }

        var noun = candidates.Count == 1 ? "account" : "accounts";
        ShowConfirmation(
            "Delete selected accounts",
            $"Delete {candidates.Count} selected {noun}? This permanently removes the selected account presets.{FormatCandidateExamples(candidates, DescribeAccountPreset)}",
            "Delete",
            () =>
            {
                RunWithPresetChangeNotificationsSuppressed(() =>
                {
                    foreach (var preset in candidates)
                    {
                        AccountPresets.Remove(preset);
                    }
                });

                if (SelectedAccountPreset is not null && candidates.Contains(SelectedAccountPreset) ||
                    _editingAccountPresetId is not null && candidates.Any(preset => preset.Id == _editingAccountPresetId))
                {
                    if (SelectedAccountPreset is not null && candidates.Contains(SelectedAccountPreset))
                    {
                        SelectedAccountPreset = null;
                    }

                    if (_editingAccountPresetId is not null && candidates.Any(preset => preset.Id == _editingAccountPresetId))
                    {
                        CloseAccountPresetEditor();
                    }
                }

                SaveNow($"Deleted {candidates.Count} selected {noun}.");
            },
            isDanger: true,
            cancelMessage: "Delete canceled.");
    }

    private void BeginNewPreset()
    {
        ResetPresetForm();
        IsPresetEditorOpen = true;
        SelectedPage = AppPage.Presets;
    }

    private void RequestNewPreset()
    {
        RequestEditorTransition(AppPage.Presets, "open a new item", BeginNewPreset, replaceCurrentEditor: true);
    }

    private void RequestEditPreset(ItemPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        RequestEditorTransition(AppPage.Presets, "edit another item", () => BeginEditPreset(preset), replaceCurrentEditor: true);
    }

    private void ToggleQuickPreset()
    {
        if (GetVisibleEditorPage() == AppPage.Presets)
        {
            ClosePresetEditor();
            LastActionMessage = "Item panel closed.";
            return;
        }

        RequestNewPreset();
    }

    private void ResetPresetForm()
    {
        _editingPresetId = null;
        SelectedPreset = null;
        PresetName = string.Empty;
        PresetCategory = string.Empty;
        PresetMerchantHint = MerchantKind.Unknown;
        PresetDefaultQuantity = 1;
        PresetDefaultUnitPrice = 0m;
        PresetDefaultShipping = 0m;
        PresetDefaultTax = 0m;
        PresetIsFavorite = false;
        PresetNotes = string.Empty;
        IsPresetAdvancedOpen = false;
        OnPropertyChanged(nameof(IsEditingPreset));
        OnPropertyChanged(nameof(PresetEditorTitle));
    }

    private void ClosePresetEditor()
    {
        IsPresetEditorOpen = false;
        _editingPresetId = null;
        OnPropertyChanged(nameof(IsEditingPreset));
        OnPropertyChanged(nameof(PresetEditorTitle));
    }

    private void BeginEditPreset(ItemPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        _editingPresetId = preset.Id;
        SelectedPreset = preset;
        PresetName = preset.Name;
        PresetCategory = preset.Category;
        PresetMerchantHint = preset.MerchantHint;
        PresetDefaultQuantity = preset.DefaultQuantity;
        PresetDefaultUnitPrice = preset.DefaultUnitPrice;
        PresetDefaultShipping = preset.DefaultShipping;
        PresetDefaultTax = preset.DefaultTax;
        PresetIsFavorite = preset.IsFavorite;
        PresetNotes = preset.Notes;
        IsPresetAdvancedOpen = !string.IsNullOrWhiteSpace(preset.Category) || preset.DefaultShipping != 0m || preset.DefaultTax != 0m || preset.IsFavorite || !string.IsNullOrWhiteSpace(preset.Notes);
        IsPresetEditorOpen = true;
        SelectedPage = AppPage.Presets;
        OnPropertyChanged(nameof(IsEditingPreset));
        OnPropertyChanged(nameof(PresetEditorTitle));
    }

    private void SavePreset()
    {
        if (!TryApplyPresetNumberInputs())
        {
            return;
        }

        var preset = ItemPresets.FirstOrDefault(candidate => candidate.Id == _editingPresetId);
        var isNew = preset is null;
        preset ??= new ItemPreset();

        RunWithPresetChangeNotificationsSuppressed(() =>
        {
            preset.Name = PresetName.Trim();
            preset.Category = PresetCategory.Trim();
            preset.MerchantHint = PresetMerchantHint;
            preset.DefaultQuantity = PresetDefaultQuantity;
            preset.DefaultUnitPrice = PresetDefaultUnitPrice;
            preset.DefaultShipping = PresetDefaultShipping;
            preset.DefaultTax = PresetDefaultTax;
            preset.IsFavorite = PresetIsFavorite;
            preset.Notes = PresetNotes.Trim();
            if (isNew)
            {
                ItemPresets.Add(preset);
            }
        });

        SelectedPreset = preset;
        SaveNow($"Item {(isNew ? "added" : "updated")}.");
        ClosePresetEditor();
    }

    private bool TryApplyPresetNumberInputs()
    {
        if (!TryParseQuantity(PresetDefaultQuantityInput, "default quantity", out var quantity) ||
            !TryParseMoney(PresetDefaultUnitPriceInput, "price", out var unitPrice) ||
            !TryParseMoney(PresetDefaultShippingInput, "shipping", out var shipping) ||
            !TryParseMoney(PresetDefaultTaxInput, "tax", out var tax))
        {
            return false;
        }

        PresetDefaultQuantity = quantity;
        PresetDefaultUnitPrice = unitPrice;
        PresetDefaultShipping = shipping;
        PresetDefaultTax = tax;
        return true;
    }

    private void DeletePreset(ItemPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        var label = DescribeItemPreset(preset);

        ShowConfirmation(
            "Delete item",
            $"Delete {label}? This permanently removes the saved item shortcut.",
            "Delete",
            () =>
            {
                RunWithPresetChangeNotificationsSuppressed(() => ItemPresets.Remove(preset));
                if (SelectedPreset == preset)
                {
                    SelectedPreset = null;
                }

                if (_editingPresetId == preset.Id)
                {
                    ClosePresetEditor();
                }

                SaveNow($"Deleted {label}.");
            },
            isDanger: true,
            cancelMessage: "Delete canceled.");
    }

    private void DuplicatePreset(ItemPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        RequestEditorTransition(
            AppPage.Presets,
            "duplicate the selected item",
            () => DuplicatePresetCore(preset),
            replaceCurrentEditor: IsPresetEditorOpen);
    }

    private void DuplicatePresetCore(ItemPreset preset)
    {

        var copy = new ItemPreset
        {
            Name = $"{preset.Name} copy".Trim(),
            Category = preset.Category,
            MerchantHint = preset.MerchantHint,
            DefaultQuantity = preset.DefaultQuantity,
            DefaultUnitPrice = preset.DefaultUnitPrice,
            DefaultShipping = preset.DefaultShipping,
            DefaultTax = preset.DefaultTax,
            IsFavorite = preset.IsFavorite,
            Notes = preset.Notes
        };

        RunWithPresetChangeNotificationsSuppressed(() => ItemPresets.Add(copy));
        SelectedPreset = copy;
        SaveNow("Item duplicated.");
        BeginEditPreset(copy);
    }

    private void SelectItemPresets(System.Collections.IEnumerable presets, bool selected)
    {
        RunWithBulkSelectionNotificationsSuppressed(() =>
        {
            foreach (var value in presets)
            {
                if (value is ItemPreset preset)
                {
                    preset.IsSelected = selected;
                }
            }
        });
    }

    private void ClearItemPresetSelection()
    {
        RunWithBulkSelectionNotificationsSuppressed(() =>
        {
            foreach (var preset in ItemPresets.Where(preset => preset.IsSelected))
            {
                preset.IsSelected = false;
            }
        });
    }

    private void DeleteSelectedPresets()
    {
        var candidates = ItemPresets
            .Where(preset => preset.IsSelected)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = "No items are selected.";
            RefreshBulkSelectionState();
            return;
        }

        var noun = candidates.Count == 1 ? "item" : "items";
        ShowConfirmation(
            "Delete selected items",
            $"Delete {candidates.Count} selected {noun}? This permanently removes the selected items.{FormatCandidateExamples(candidates, DescribeItemPreset)}",
            "Delete",
            () =>
            {
                RunWithPresetChangeNotificationsSuppressed(() =>
                {
                    foreach (var preset in candidates)
                    {
                        ItemPresets.Remove(preset);
                    }
                });

                if (SelectedPreset is not null && candidates.Contains(SelectedPreset) ||
                    _editingPresetId is not null && candidates.Any(preset => preset.Id == _editingPresetId))
                {
                    if (SelectedPreset is not null && candidates.Contains(SelectedPreset))
                    {
                        SelectedPreset = null;
                    }

                    if (_editingPresetId is not null && candidates.Any(preset => preset.Id == _editingPresetId))
                    {
                        ClosePresetEditor();
                    }
                }

                SaveNow($"Deleted {candidates.Count} selected {noun}.");
            },
            isDanger: true,
            cancelMessage: "Delete canceled.");
    }

    private async System.Threading.Tasks.Task SendDiscordStatsAsync()
    {
        IsBusy = true;
        try
        {
            LastActionMessage = await _discordWebhookService.SendStatsAsync(Orders, Settings);
        }
        catch (Exception)
        {
            LastActionMessage = "Discord send failed. Check the webhook URL or network connection.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool FilterOrder(object value)
    {
        if (value is not Order order)
        {
            return false;
        }

        if (order.IsArchived)
        {
            return false;
        }

        if (HideCompleted && order.IsFinal)
        {
            return false;
        }

        var today = DateTime.Today;
        var matchesAttentionFilter = SelectedAttentionFilter switch
        {
            OrderAttentionFilter.Overdue => OrderState.IsOverdue(order.Status, order.ExpectedDate, today),
            OrderAttentionFilter.ExpectedToday => order.IsOpen && order.ExpectedDate?.Date == today,
            OrderAttentionFilter.MissingTracking => order.IsOpen && !order.HasTrackingNumbers,
            OrderAttentionFilter.ReadyToArchive => order.CanArchive,
            _ => true
        };

        if (!matchesAttentionFilter)
        {
            return false;
        }

        return MatchesOrderSearch(order, SearchText);
    }

    private bool FilterArchivedOrder(object value)
    {
        if (value is not Order order)
        {
            return false;
        }

        return order.IsArchived && MatchesOrderSearch(order, ArchiveSearchText);
    }

    private static bool MatchesOrderSearch(Order order, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var itemText = string.Join(" ", order.ItemsSummary, order.Items.Select(item => item.Name));
        var haystack = string.Join(" ", order.AccountEmail, order.Merchant, order.OrderNumber, itemText, order.Status, order.TrackingStatus, order.Notes, string.Join(" ", order.TrackingNumbers.Select(tracking => tracking.Number)));
        return haystack.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<Order> GetVisibleOrdersWithItems()
    {
        return OrdersView
            .Cast<object>()
            .OfType<Order>()
            .Where(order => order.HasItems);
    }

    private bool ShouldCollapseVisibleOrderItems()
    {
        var visibleOrdersWithItems = GetVisibleOrdersWithItems().ToList();
        return visibleOrdersWithItems.Count > 0 && visibleOrdersWithItems.All(order => order.IsItemsExpanded);
    }

    private bool FilterPreset(object value)
    {
        if (value is not ItemPreset preset)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(PresetSearchText))
        {
            return true;
        }

        var haystack = string.Join(" ", preset.Name, preset.Category, preset.MerchantHint, preset.Notes);
        return haystack.Contains(PresetSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterOrderItemPreset(object value)
    {
        return value is ItemPreset preset &&
               PresetWorkflowRules.IsMerchantQuickFillMatch(FormMerchant, preset.MerchantHint);
    }

    private bool FilterAccountPreset(object value)
    {
        if (value is not AccountPreset preset)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(AccountPresetSearchText))
        {
            return true;
        }

        var haystack = string.Join(" ", preset.Name, preset.Email, preset.MerchantHint, preset.Notes);
        return haystack.Contains(AccountPresetSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private AccountPreset? FindMatchingAccountPreset(Order order)
    {
        var accountEmail = order.AccountEmail.Trim();
        if (string.IsNullOrWhiteSpace(accountEmail))
        {
            return null;
        }

        return AccountPresets.FirstOrDefault(candidate =>
            candidate.MerchantHint == order.Merchant &&
            string.Equals(candidate.Email.Trim(), accountEmail, StringComparison.OrdinalIgnoreCase)) ??
            AccountPresets.FirstOrDefault(candidate =>
                string.Equals(candidate.Email.Trim(), accountEmail, StringComparison.OrdinalIgnoreCase));
    }

    private bool RefreshAccountUsageCounts()
    {
        var counts = AccountPresets.ToDictionary(preset => preset, _ => 0);
        foreach (var order in Orders)
        {
            var preset = FindMatchingAccountPreset(order);
            if (preset is not null)
            {
                counts[preset]++;
            }
        }

        var changed = false;
        RunWithPresetChangeNotificationsSuppressed(() =>
        {
            foreach (var (preset, count) in counts)
            {
                if (preset.UsageCount != count)
                {
                    preset.UsageCount = count;
                    changed = true;
                }
            }
        });

        RefreshAccountUsageAuditOrders();
        return changed;
    }

    private void OpenAccountUsageAudit(AccountPreset? preset)
    {
        if (preset is null || !AccountPresets.Contains(preset))
        {
            return;
        }

        RefreshAccountUsageCounts();
        _accountUsageAuditPreset = preset;
        RefreshAccountUsageAuditOrders();
        OnPropertyChanged(nameof(AccountUsageAuditTitle));
        IsAccountUsageAuditOpen = true;
    }

    private void CloseAccountUsageAudit()
    {
        IsAccountUsageAuditOpen = false;
        _accountUsageAuditPreset = null;
        AccountUsageAuditOrders.Clear();
        OnPropertyChanged(nameof(AccountUsageAuditTitle));
        OnPropertyChanged(nameof(AccountUsageAuditSummary));
    }

    private void RefreshAccountUsageAuditOrders()
    {
        if (_accountUsageAuditPreset is null)
        {
            return;
        }

        var matchingOrders = Orders
            .Where(order => ReferenceEquals(FindMatchingAccountPreset(order), _accountUsageAuditPreset))
            .OrderBy(order => order.IsArchived)
            .ThenByDescending(order => order.CreatedAt)
            .ToList();

        AccountUsageAuditOrders.Clear();
        foreach (var order in matchingOrders)
        {
            AccountUsageAuditOrders.Add(order);
        }

        OnPropertyChanged(nameof(AccountUsageAuditSummary));
    }

    private void OpenAccountUsageOrder(Order? order)
    {
        if (order is null || !Orders.Contains(order))
        {
            return;
        }

        CloseAccountUsageAudit();
        var targetPage = order.IsArchived ? AppPage.Archive : AppPage.Orders;
        RequestEditorTransition(
            targetPage,
            "open the selected account-usage order",
            () => RevealAccountUsageOrder(order));
    }

    private void RevealAccountUsageOrder(Order order)
    {
        if (order.IsArchived)
        {
            ArchiveSearchText = string.Empty;
            SelectedPage = AppPage.Archive;
        }
        else
        {
            SearchText = string.Empty;
            HideCompleted = false;
            SelectedPage = AppPage.Orders;
        }

        SelectedOrder = null;
        SelectedOrder = order;
        OrderRevealRequested?.Invoke(order);
        LastActionMessage = $"Opened {DescribeOrder(order)} from account usage.";
    }

    private bool FilterOrderAccountPreset(object value)
    {
        return value is AccountPreset preset && IsOrderAccountPresetVisible(preset);
    }

    private bool IsOrderAccountPresetVisible(AccountPreset preset)
    {
        return PresetWorkflowRules.IsMerchantQuickFillMatch(FormMerchant, preset.MerchantHint);
    }

    private void RefreshOrderAccountPresets()
    {
        OrderAccountPresetsView.Refresh();
        if (SelectedOrderAccountPreset is not null &&
            (!AccountPresets.Contains(SelectedOrderAccountPreset) || !IsOrderAccountPresetVisible(SelectedOrderAccountPreset)))
        {
            SelectedOrderAccountPreset = null;
        }
    }

    private void ApplySortAndGroup()
    {
        using (OrdersView.DeferRefresh())
        {
            OrdersView.SortDescriptions.Clear();
            OrdersView.GroupDescriptions.Clear();

            switch (SelectedGroup)
            {
                case OrderGroupOption.Account:
                    OrdersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Order.AccountEmail)));
                    break;
                case OrderGroupOption.Item:
                    OrdersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Order.PrimaryItem)));
                    break;
                case OrderGroupOption.Merchant:
                    OrdersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Order.Merchant)));
                    break;
                case OrderGroupOption.Status:
                    OrdersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Order.Status)));
                    break;
                case OrderGroupOption.Month:
                    OrdersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Order.OrderMonth)));
                    break;
                case OrderGroupOption.Year:
                    OrdersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Order.OrderYear)));
                    break;
            }

            foreach (var sort in GetSortDescriptions())
            {
                OrdersView.SortDescriptions.Add(sort);
            }
        }

        OnPropertyChanged(nameof(ShowOrderGroupHeaders));
    }

    private void ApplyArchiveSort()
    {
        using (ArchivedOrdersView.DeferRefresh())
        {
            ArchivedOrdersView.SortDescriptions.Clear();
            ArchivedOrdersView.SortDescriptions.Add(new SortDescription(nameof(Order.DeliveredDate), ListSortDirection.Descending));
            ArchivedOrdersView.SortDescriptions.Add(new SortDescription(nameof(Order.OrderDate), ListSortDirection.Descending));
        }
    }

    private void ApplyAccountPresetSortAndGroup()
    {
        var sortDescriptions = GetAccountPresetSortDescriptions().ToList();
        var groupDescriptions = GetAccountPresetGroupDescriptions().ToList();

        ApplyViewSortAndGroup(AccountPresetsView, sortDescriptions, groupDescriptions);
        ApplyViewSortAndGroup(OrderAccountPresetsView, sortDescriptions);
        OnPropertyChanged(nameof(ShowAccountGroupHeaders));
    }

    private void ApplyItemPresetSortAndGroup()
    {
        var sortDescriptions = GetItemPresetSortDescriptions().ToList();
        var groupDescriptions = GetItemPresetGroupDescriptions().ToList();

        ApplyViewSortAndGroup(PresetsView, sortDescriptions, groupDescriptions);
        OnPropertyChanged(nameof(ShowItemGroupHeaders));
    }

    private static void ApplyViewSortAndGroup(
        ICollectionView view,
        IEnumerable<SortDescription> sortDescriptions,
        IEnumerable<GroupDescription>? groupDescriptions = null)
    {
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Clear();

            if (groupDescriptions is not null)
            {
                foreach (var group in groupDescriptions)
                {
                    view.GroupDescriptions.Add(group);
                }
            }

            foreach (var sort in sortDescriptions)
            {
                view.SortDescriptions.Add(sort);
            }
        }
    }

    private IEnumerable<SortDescription> GetSortDescriptions()
    {
        return SelectedSort switch
        {
            OrderSortOption.OldestFirst => new[] { new SortDescription(nameof(Order.OrderDate), ListSortDirection.Ascending) },
            OrderSortOption.NewestCreated => new[]
            {
                new SortDescription(nameof(Order.CreatedAt), ListSortDirection.Descending)
            },
            OrderSortOption.OldestCreated => new[]
            {
                new SortDescription(nameof(Order.CreatedAt), ListSortDirection.Ascending)
            },
            OrderSortOption.ExpectedSoonest => new[] { new SortDescription(nameof(Order.ExpectedSortDate), ListSortDirection.Ascending), new SortDescription(nameof(Order.OrderDate), ListSortDirection.Descending) },
            OrderSortOption.Merchant => new[] { new SortDescription(nameof(Order.Merchant), ListSortDirection.Ascending), new SortDescription(nameof(Order.OrderDate), ListSortDirection.Descending) },
            OrderSortOption.Account => new[] { new SortDescription(nameof(Order.AccountEmail), ListSortDirection.Ascending), new SortDescription(nameof(Order.OrderDate), ListSortDirection.Descending) },
            OrderSortOption.Item => new[] { new SortDescription(nameof(Order.PrimaryItem), ListSortDirection.Ascending), new SortDescription(nameof(Order.OrderDate), ListSortDirection.Descending) },
            OrderSortOption.Status => new[] { new SortDescription(nameof(Order.Status), ListSortDirection.Ascending), new SortDescription(nameof(Order.OrderDate), ListSortDirection.Descending) },
            OrderSortOption.TotalHighToLow => new[] { new SortDescription(nameof(Order.TotalCost), ListSortDirection.Descending) },
            OrderSortOption.TotalLowToHigh => new[] { new SortDescription(nameof(Order.TotalCost), ListSortDirection.Ascending) },
            _ => new[] { new SortDescription(nameof(Order.OrderDate), ListSortDirection.Descending) }
        };
    }

    private IEnumerable<SortDescription> GetAccountPresetSortDescriptions()
    {
        return SelectedAccountSort switch
        {
            AccountSortOption.NameDescending => new[]
            {
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Descending),
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            },
            AccountSortOption.EmailAscending => new[]
            {
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            },
            AccountSortOption.EmailDescending => new[]
            {
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Descending),
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            },
            AccountSortOption.MerchantAscending => new[]
            {
                new SortDescription(nameof(AccountPreset.MerchantHint), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            },
            AccountSortOption.FavoritesFirst => new[]
            {
                new SortDescription(nameof(AccountPreset.IsFavorite), ListSortDirection.Descending),
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            },
            AccountSortOption.MostUsed => new[]
            {
                new SortDescription(nameof(AccountPreset.UsageCount), ListSortDirection.Descending),
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            },
            AccountSortOption.LeastUsed => new[]
            {
                new SortDescription(nameof(AccountPreset.UsageCount), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            },
            AccountSortOption.NewestCreated => new[]
            {
                new SortDescription(nameof(AccountPreset.CreatedAt), ListSortDirection.Descending)
            },
            AccountSortOption.OldestCreated => new[]
            {
                new SortDescription(nameof(AccountPreset.CreatedAt), ListSortDirection.Ascending)
            },
            _ => new[]
            {
                new SortDescription(nameof(AccountPreset.DisplayName), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Email), ListSortDirection.Ascending),
                new SortDescription(nameof(AccountPreset.Id), ListSortDirection.Ascending)
            }
        };
    }

    private IEnumerable<GroupDescription> GetAccountPresetGroupDescriptions()
    {
        return SelectedAccountGroup switch
        {
            AccountGroupOption.Merchant => new[] { new PropertyGroupDescription(nameof(AccountPreset.MerchantHint)) },
            AccountGroupOption.Favorite => new[] { new PropertyGroupDescription(nameof(AccountPreset.FavoriteGroup)) },
            AccountGroupOption.Usage => new[] { new PropertyGroupDescription(nameof(AccountPreset.UsageGroup)) },
            AccountGroupOption.EmailDomain => new[] { new PropertyGroupDescription(nameof(AccountPreset.EmailDomainGroup)) },
            _ => Array.Empty<GroupDescription>()
        };
    }

    private IEnumerable<SortDescription> GetItemPresetSortDescriptions()
    {
        return SelectedItemSort switch
        {
            ItemSortOption.NameDescending => new[]
            {
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Descending),
                new SortDescription(nameof(ItemPreset.Category), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.CategoryAscending => new[]
            {
                new SortDescription(nameof(ItemPreset.Category), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.MerchantAscending => new[]
            {
                new SortDescription(nameof(ItemPreset.MerchantHint), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.FavoritesFirst => new[]
            {
                new SortDescription(nameof(ItemPreset.IsFavorite), ListSortDirection.Descending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.MostUsed => new[]
            {
                new SortDescription(nameof(ItemPreset.UsageCount), ListSortDirection.Descending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.LeastUsed => new[]
            {
                new SortDescription(nameof(ItemPreset.UsageCount), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.PriceLowToHigh => new[]
            {
                new SortDescription(nameof(ItemPreset.DefaultUnitPrice), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.PriceHighToLow => new[]
            {
                new SortDescription(nameof(ItemPreset.DefaultUnitPrice), ListSortDirection.Descending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.QuantityLowToHigh => new[]
            {
                new SortDescription(nameof(ItemPreset.DefaultQuantity), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            ItemSortOption.QuantityHighToLow => new[]
            {
                new SortDescription(nameof(ItemPreset.DefaultQuantity), ListSortDirection.Descending),
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            },
            _ => new[]
            {
                new SortDescription(nameof(ItemPreset.Name), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Category), ListSortDirection.Ascending),
                new SortDescription(nameof(ItemPreset.Id), ListSortDirection.Ascending)
            }
        };
    }

    private IEnumerable<GroupDescription> GetItemPresetGroupDescriptions()
    {
        return SelectedItemGroup switch
        {
            ItemGroupOption.Category => new[] { new PropertyGroupDescription(nameof(ItemPreset.CategoryGroup)) },
            ItemGroupOption.Merchant => new[] { new PropertyGroupDescription(nameof(ItemPreset.MerchantHint)) },
            ItemGroupOption.Favorite => new[] { new PropertyGroupDescription(nameof(ItemPreset.FavoriteGroup)) },
            ItemGroupOption.Usage => new[] { new PropertyGroupDescription(nameof(ItemPreset.UsageGroup)) },
            ItemGroupOption.PriceRange => new[] { new PropertyGroupDescription(nameof(ItemPreset.PriceRangeGroup)) },
            _ => Array.Empty<GroupDescription>()
        };
    }

    private void RefreshDashboard()
    {
        var orders = Orders.ToList();
        RefreshMerchantRoiSettingsState();

        MetricCards.Clear();
        foreach (var card in BuildMetricCards(orders))
        {
            MetricCards.Add(card);
        }

        ReplaceMonthlyComparison(MonthlyComparison, BuildMonthlyComparison(orders));
        ReplaceChart(MerchantSpend, BuildMerchantSpend(orders));
        ReplaceChart(StatusBreakdown, BuildStatusBreakdown(orders));
        RefreshSidebarAlerts(orders);
    }

    private void RefreshMerchantRoiSettingsState()
    {
        OnPropertyChanged(nameof(ActiveMerchantRoiSettings));
        OnPropertyChanged(nameof(InactiveMerchantRoiSettings));
    }

    private void RefreshSidebarAlerts()
    {
        RefreshSidebarAlerts(Orders.ToList());
    }

    private void RefreshSidebarAlerts(IReadOnlyCollection<Order> orders)
    {
        ReplaceSidebarItems(SidebarAlerts, BuildSidebarAlerts(orders));
    }

    private IEnumerable<SidebarPanelItem> BuildSidebarAlerts(IReadOnlyCollection<Order> orders)
    {
        var today = DateTime.Today;
        var openOrders = orders.Where(order => !order.IsArchived && order.IsOpen).ToList();
        var alerts = new List<SidebarPanelItem>();

        var overdueOrders = openOrders.Count(order => OrderState.IsOverdue(order.Status, order.ExpectedDate, today));
        if (overdueOrders > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Past expected",
                Detail = $"{FormatSimpleCount(overdueOrders, "order")} past expected date.",
                Accent = "#E05D5D",
                Command = ApplyOrderAttentionFilterCommand,
                CommandParameter = OrderAttentionFilter.Overdue
            });
        }

        var dueTodayOrders = openOrders.Count(order => order.ExpectedDate?.Date == today);
        if (dueTodayOrders > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Expected today",
                Detail = $"{FormatSimpleCount(dueTodayOrders, "order")} may land today.",
                Accent = "#FFB547",
                Command = ApplyOrderAttentionFilterCommand,
                CommandParameter = OrderAttentionFilter.ExpectedToday
            });
        }

        var completedOrdersReadyToArchive = orders.Count(order => order.CanArchive && !order.IsArchived);
        if (completedOrdersReadyToArchive > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Ready to archive",
                Detail = $"{FormatSimpleCount(completedOrdersReadyToArchive, "order")} can move off Orders.",
                Accent = "#2F9E7E",
                Command = ApplyOrderAttentionFilterCommand,
                CommandParameter = OrderAttentionFilter.ReadyToArchive
            });
        }

        var missingTrackingOrders = openOrders.Count(order => !order.HasTrackingNumbers);
        if (missingTrackingOrders > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Missing tracking",
                Detail = $"{FormatSimpleCount(missingTrackingOrders, "open order")} without tracking.",
                Accent = "#7C9BFF",
                Command = ApplyOrderAttentionFilterCommand,
                CommandParameter = OrderAttentionFilter.MissingTracking
            });
        }

        var selectedCount =
            orders.Count(order => order.IsSelected && !order.IsArchived) +
            orders.Count(order => order.IsSelected && order.IsArchived) +
            SelectedAccountPresetCount +
            SelectedPresetCount;
        if (selectedCount > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Selection active",
                Detail = $"{FormatSimpleCount(selectedCount, "item")} selected across pages.",
                Accent = "#B389FF"
            });
        }

        var legacyOrderItemMigrationCount = orders.Count(NeedsLegacyItemMigration);
        if (legacyOrderItemMigrationCount > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Config update",
                Detail = $"{FormatSimpleCount(legacyOrderItemMigrationCount, "order")} awaiting item migration.",
                Accent = "#F57FB0"
            });
        }

        return alerts.Count == 0
            ? new[]
            {
                new SidebarPanelItem
                {
                    Label = "All clear",
                    Detail = "No overdue, selected, or migration items.",
                    Accent = "#2F9E7E"
                }
            }
            : alerts.Take(5);
    }

    private IEnumerable<MetricCard> BuildMetricCards(IReadOnlyCollection<Order> orders)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var activeOpenOrders = orders.Where(order => !order.IsArchived && order.IsOpen).ToList();
        var openOrders = activeOpenOrders.Count;
        var openBalance = activeOpenOrders.Sum(order => order.TotalCost);
        var monthOrders = orders.Where(order => order.OrderDate >= monthStart && order.OrderDate < monthEnd).ToList();
        var monthSpend = monthOrders.Sum(order => order.TotalCost);
        var projectedMonthRoi = CalculateProjectedRoi(monthOrders);
        var monthEffectiveRoiPercent = CalculateEffectiveRoiPercent(monthSpend, projectedMonthRoi);

        return new[]
        {
            new MetricCard { Label = "Open orders", Value = openOrders.ToString(CultureInfo.CurrentCulture), Detail = "Still moving or waiting", Accent = "#5CC8FF" },
            new MetricCard { Label = "Open balance", Value = openBalance.ToString("C", CultureInfo.CurrentCulture), Detail = "Not yet completed", Accent = "#F57FB0" },
            new MetricCard { Label = "This month", Value = monthSpend.ToString("C", CultureInfo.CurrentCulture), Detail = "Orders placed this month", Accent = "#FFB547" },
            new MetricCard { Label = "Projected month ROI", Value = projectedMonthRoi.ToString("C", CultureInfo.CurrentCulture), Detail = $"{FormatPercent(monthEffectiveRoiPercent)} effective ROI rate", Accent = "#2F9E7E" }
        };
    }

    private IEnumerable<MonthlyComparisonPoint> BuildMonthlyComparison(IReadOnlyCollection<Order> orders)
    {
        var range = Settings.DashboardMonthRange is 3 or 6 or 12 ? Settings.DashboardMonthRange : 3;
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(range - 1));
        var points = Enumerable.Range(0, range)
            .Select(offset => start.AddMonths(offset))
            .Select(month =>
            {
                var next = month.AddMonths(1);
                var monthOrders = orders.Where(order => order.OrderDate >= month && order.OrderDate < next).ToList();
                var spend = monthOrders.Sum(order => order.TotalCost);
                var roi = monthOrders.Sum(Settings.GetProjectedRoiAmount);
                return new MonthlyComparisonPoint
                {
                    Label = month.ToString("MMM yy", CultureInfo.CurrentCulture),
                    Spend = spend,
                    ProjectedRoi = roi,
                    SpendDisplay = spend.ToString("C0", CultureInfo.CurrentCulture),
                    RoiDisplay = roi.ToString("C0", CultureInfo.CurrentCulture)
                };
            })
            .ToList();

        var spendMaximum = Math.Max(0m, points.Max(point => point.Spend));
        var roiMaximum = Math.Max(0m, points.Max(point => point.ProjectedRoi));
        foreach (var point in points)
        {
            point.SpendPercent = spendMaximum <= 0m ? 0d : (double)(point.Spend / spendMaximum * 100m);
            point.RoiPercent = roiMaximum <= 0m ? 0d : (double)(point.ProjectedRoi / roiMaximum * 100m);
        }

        return points;
    }

    private decimal CalculateProjectedRoi(IEnumerable<Order> orders)
    {
        return orders.Sum(Settings.GetProjectedRoiAmount);
    }

    private static decimal CalculateEffectiveRoiPercent(decimal spend, decimal projectedRoi)
    {
        return AppSettings.CalculateEffectiveProjectedRoiPercent(spend, projectedRoi);
    }

    private static string FormatPercent(decimal percent)
    {
        return string.Concat(Math.Max(0m, percent).ToString("0.##", CultureInfo.CurrentCulture), "%");
    }

    private IEnumerable<ChartPoint> BuildMerchantSpend(IReadOnlyCollection<Order> orders)
    {
        var points = orders
            .GroupBy(order => order.Merchant)
            .OrderByDescending(group => group.Sum(order => order.TotalCost))
            .Take(8)
            .Select(group =>
            {
                var value = group.Sum(order => order.TotalCost);
                return new ChartPoint
                {
                    Label = EnumDisplayFormatter.Format(group.Key),
                    Value = value,
                    DisplayValue = value.ToString("C0", CultureInfo.CurrentCulture),
                    Accent = GetMerchantAccent(group.Key)
                };
            })
            .DefaultIfEmpty(new ChartPoint { Label = "No orders", DisplayValue = "No spend", Accent = "#6B7A90" })
            .ToList();

        ApplyPercents(points);
        return points;
    }

    private IEnumerable<ChartPoint> BuildStatusBreakdown(IReadOnlyCollection<Order> orders)
    {
        var totalOrders = orders.Count;
        var points = Enum.GetValues<OrderStatus>()
            .Select(status =>
            {
                var count = orders.Count(order => order.Status == status);
                return new ChartPoint
                {
                    Label = EnumDisplayFormatter.Format(status),
                    Value = count,
                    DisplayValue = count.ToString(CultureInfo.CurrentCulture),
                    Accent = GetStatusAccent(status)
                };
            })
            .Where(point => point.Value > 0)
            .DefaultIfEmpty(new ChartPoint { Label = "No orders", DisplayValue = "0", Accent = "#6B7A90" })
            .ToList();

        ApplyStatusPercents(points, totalOrders);
        return points;
    }

    private static void ApplyPercents(IList<ChartPoint> points)
    {
        var max = points.Count == 0 ? 0m : points.Max(point => point.Value);
        foreach (var point in points)
        {
            point.Percent = max <= 0 || point.Value <= 0 ? 0 : Math.Max(4, (double)(point.Value / max * 100));
        }
    }

    private static void ApplyStatusPercents(IList<ChartPoint> points, int totalOrders)
    {
        foreach (var point in points)
        {
            point.Percent = totalOrders <= 0 ? 0 : Math.Max(6, (double)(point.Value / totalOrders * 100));
        }
    }

    private static string GetStatusAccent(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Delivered => "#2F9E7E",
            OrderStatus.OutForDelivery => "#5CC8FF",
            OrderStatus.Shipped => "#7C9BFF",
            OrderStatus.Delayed => "#F5A524",
            OrderStatus.Cancelled or OrderStatus.Returned => "#E05D5D",
            OrderStatus.Processing => "#B389FF",
            _ => "#6B7A90"
        };
    }

    private static string GetMerchantAccent(MerchantKind merchant)
    {
        return merchant switch
        {
            MerchantKind.Amazon => "#FFB547",
            MerchantKind.Walmart => "#5CC8FF",
            MerchantKind.Target => "#E05D5D",
            MerchantKind.BestBuy => "#7C9BFF",
            MerchantKind.eBay => "#B389FF",
            MerchantKind.Other => "#2F9E7E",
            _ => "#6B7A90"
        };
    }

    private static void ReplaceChart(ObservableCollection<ChartPoint> target, IEnumerable<ChartPoint> source)
    {
        target.Clear();
        foreach (var point in source)
        {
            target.Add(point);
        }
    }

    private static void ReplaceSidebarItems(ObservableCollection<SidebarPanelItem> target, IEnumerable<SidebarPanelItem> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static void ReplaceMonthlyComparison(ObservableCollection<MonthlyComparisonPoint> target, IEnumerable<MonthlyComparisonPoint> source)
    {
        target.Clear();
        foreach (var point in source)
        {
            target.Add(point);
        }
    }

    private void StartAutosaveTimer()
    {
        _autosaveTimer.Interval = AutosaveDelay;
        _autosaveTimer.Tick += AutosaveTimerTick;
    }

    private void AutosaveTimerTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        SaveNow();
    }

    private void StartSidebarClock()
    {
        UpdateSidebarClock();
        _sidebarClockTimer.Interval = TimeSpan.FromSeconds(1);
        _sidebarClockTimer.Tick += SidebarClockTimerTick;
        _sidebarClockTimer.Start();
    }

    private void SidebarClockTimerTick(object? sender, EventArgs e)
    {
        UpdateSidebarClock();
    }

    private async Task SyncSidebarClockAsync()
    {
        var networkUtcTime = await _networkTimeService.TryGetUtcTimeAsync();
        if (networkUtcTime is null || _isDisposed)
        {
            return;
        }

        _networkClockUtc = networkUtcTime.Value;
        _networkClockTimestamp = Stopwatch.GetTimestamp();
        UpdateSidebarClock();
    }

    private void UpdateSidebarClock()
    {
        if (_isDisposed)
        {
            return;
        }

        var previousDate = _sidebarDateTime.Date;
        _sidebarDateTime = GetSidebarDateTime();
        OnPropertyChanged(nameof(SidebarDate));
        OnPropertyChanged(nameof(SidebarTime));

        if (_sidebarDateTime.Date != previousDate)
        {
            RefreshForLocalDateChange();
        }
    }

    private void RefreshForLocalDateChange()
    {
        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            foreach (var order in Orders)
            {
                order.RefreshDateDependentProperties();
            }
        });

        RefreshOrderViews();
        RefreshDashboard();
        RefreshArchiveState();
    }

    private DateTime GetSidebarDateTime()
    {
        var utcNow = _networkClockUtc is { } networkClockUtc
            ? networkClockUtc + Stopwatch.GetElapsedTime(_networkClockTimestamp)
            : DateTimeOffset.UtcNow;

        return (utcNow + SidebarClockDisplayOffset).ToLocalTime().DateTime;
    }

    private static string DescribeOrder(Order order)
    {
        var primaryItem = order.PrimaryItem.Trim();
        var usedItemAsAnchor = false;
        string label;

        if (!string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            label = $"order {order.OrderNumber.Trim()}";
        }
        else if (!string.IsNullOrWhiteSpace(primaryItem))
        {
            label = $"order for '{primaryItem}'";
            usedItemAsAnchor = true;
        }
        else
        {
            label = "this order";
        }

        var details = new List<string>();
        if (order.Merchant != MerchantKind.Unknown)
        {
            details.Add(EnumDisplayFormatter.Format(order.Merchant));
        }

        if (!usedItemAsAnchor && !string.IsNullOrWhiteSpace(primaryItem))
        {
            details.Add(primaryItem);
        }

        if (!string.IsNullOrWhiteSpace(order.AccountEmail))
        {
            details.Add(order.AccountEmail.Trim());
        }

        return details.Count == 0
            ? label
            : $"{label} ({string.Join(", ", details)})";
    }

    private static string DescribeAccountPreset(AccountPreset preset)
    {
        var displayName = preset.DisplayName.Trim();
        var label = string.IsNullOrWhiteSpace(displayName)
            ? "this account preset"
            : $"account preset '{displayName}'";

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(preset.Email) &&
            !string.Equals(preset.Email.Trim(), displayName, StringComparison.OrdinalIgnoreCase))
        {
            details.Add(preset.Email.Trim());
        }

        if (preset.MerchantHint != MerchantKind.Unknown)
        {
            details.Add(EnumDisplayFormatter.Format(preset.MerchantHint));
        }

        return details.Count == 0
            ? label
            : $"{label} ({string.Join(", ", details)})";
    }

    private static string DescribeItemPreset(ItemPreset preset)
    {
        var label = string.IsNullOrWhiteSpace(preset.Name)
            ? "this item"
            : $"item '{preset.Name.Trim()}'";

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(preset.Category))
        {
            details.Add(preset.Category.Trim());
        }

        if (preset.MerchantHint != MerchantKind.Unknown)
        {
            details.Add(EnumDisplayFormatter.Format(preset.MerchantHint));
        }

        return details.Count == 0
            ? label
            : $"{label} ({string.Join(", ", details)})";
    }

    private static string FormatCandidateExamples<T>(IReadOnlyCollection<T> candidates, Func<T, string> describe)
    {
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        var examples = candidates
            .Take(3)
            .Select(describe)
            .Where(example => !string.IsNullOrWhiteSpace(example))
            .ToList();

        if (examples.Count == 0)
        {
            return string.Empty;
        }

        var more = candidates.Count > examples.Count
            ? $" and {candidates.Count - examples.Count} more"
            : string.Empty;
        var prefix = candidates.Count == 1 ? " This affects " : " Includes ";
        return $"{prefix}{string.Join("; ", examples)}{more}.";
    }

    private void ShowConfirmation(
        string title,
        string message,
        string confirmText,
        Action confirmed,
        bool isDanger = false,
        string cancelText = "Cancel",
        string? cancelMessage = null)
    {
        _pendingConfirmationAction = confirmed;
        _pendingConfirmationCancelMessage = cancelMessage;
        ConfirmationTitle = title;
        ConfirmationMessage = message;
        ConfirmationConfirmText = confirmText;
        ConfirmationCancelText = cancelText;
        ConfirmationIsDanger = isDanger;
        IsConfirmationOpen = true;
    }

    private void ConfirmDialog()
    {
        var action = _pendingConfirmationAction;
        CloseConfirmation();
        action?.Invoke();
    }

    private void CancelDialog()
    {
        var message = _pendingConfirmationCancelMessage;
        CloseConfirmation();
        if (!string.IsNullOrWhiteSpace(message))
        {
            LastActionMessage = message;
        }
    }

    private void CloseConfirmation()
    {
        IsConfirmationOpen = false;
        _pendingConfirmationAction = null;
        _pendingConfirmationCancelMessage = null;
        ConfirmationTitle = string.Empty;
        ConfirmationMessage = string.Empty;
        ConfirmationConfirmText = "Confirm";
        ConfirmationCancelText = "Cancel";
        ConfirmationIsDanger = false;
    }

    private static string FormatSelectedCount(int count, string singularName)
    {
        return count == 0
            ? $"0 {singularName}s selected."
            : count == 1
                ? $"1 {singularName} selected."
                : $"{count} {singularName}s selected.";
    }

    private static string FormatSimpleCount(int count, string singularName)
    {
        var noun = count == 1 ? singularName : $"{singularName}s";
        return $"{count.ToString(CultureInfo.CurrentCulture)} {noun}";
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Settings && e.PropertyName == nameof(AppSettings.IsSidebarCollapsed))
        {
            OnPropertyChanged(nameof(SidebarWidth));
            OnPropertyChanged(nameof(IsSidebarExpanded));
        }

        if (sender == Settings && e.PropertyName == nameof(AppSettings.DiscordWebhookUrl))
        {
            OnPropertyChanged(nameof(DiscordWebhookConfigurationStatus));
            ((RelayCommand)ClearDiscordWebhookCommand).RaiseCanExecuteChanged();
        }

        if (sender == Settings.Columns && e.PropertyName == nameof(ColumnSettings.ShowMerchant))
        {
            OnPropertyChanged(nameof(ShowOrderMerchantColumn));
        }

        if (sender == Settings && e.PropertyName == nameof(AppSettings.UseAccountBrowserSessions))
        {
            RaiseAccountActionCommandState();
        }

        if (sender == Settings &&
            e.PropertyName is nameof(AppSettings.WindowWidth)
                or nameof(AppSettings.WindowHeight)
                or nameof(AppSettings.WindowLeft)
                or nameof(AppSettings.WindowTop)
                or nameof(AppSettings.IsWindowMaximized)
                or nameof(AppSettings.BrowserLinkWindowWidth)
                or nameof(AppSettings.BrowserLinkWindowHeight)
                or nameof(AppSettings.BrowserLinkWindowLeft)
                or nameof(AppSettings.BrowserLinkWindowTop))
        {
            return;
        }

        if (sender == Settings && e.PropertyName == nameof(AppSettings.AutoSave))
        {
            SaveNow();
            RefreshCurrentCommandState();
            return;
        }

        if (sender == Settings && e.PropertyName == nameof(AppSettings.FetchMerchantFavicons))
        {
            RefreshMerchantIconCacheState(refreshIcons: !Settings.FetchMerchantFavicons);
            if (Settings.FetchMerchantFavicons)
            {
                QueueMerchantFaviconFetch();
            }
            else
            {
                _pendingMerchantFaviconFetches.Clear();
            }
        }

        PersistIfNeeded();
    }

    private void MerchantProjectedRoiPercentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (MerchantRoiSetting setting in e.OldItems)
            {
                setting.PropertyChanged -= MerchantProjectedRoiSettingPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (MerchantRoiSetting setting in e.NewItems)
            {
                setting.PropertyChanged += MerchantProjectedRoiSettingPropertyChanged;
            }
        }

        RefreshDashboard();
        PersistIfNeeded();
    }

    private void MerchantProjectedRoiSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshDashboard();
        RefreshOrderPreview();
        PersistIfNeeded();
    }

    private void SubscribeToOrder(Order order)
    {
        order.PropertyChanged -= OrderPropertyChanged;
        order.PropertyChanged += OrderPropertyChanged;
        AttachTrackingCollection(order);
    }

    private void AttachTrackingCollection(Order order)
    {
        if (_subscribedTrackingCollections.TryGetValue(order, out var previousCollection) &&
            !ReferenceEquals(previousCollection, order.TrackingNumbers))
        {
            previousCollection.CollectionChanged -= TrackingNumbersCollectionChanged;
        }

        order.TrackingNumbers.CollectionChanged -= TrackingNumbersCollectionChanged;
        order.TrackingNumbers.CollectionChanged += TrackingNumbersCollectionChanged;
        _subscribedTrackingCollections[order] = order.TrackingNumbers;
    }

    private void UnsubscribeFromOrder(Order order)
    {
        order.PropertyChanged -= OrderPropertyChanged;
        if (_subscribedTrackingCollections.Remove(order, out var trackingNumbers))
        {
            trackingNumbers.CollectionChanged -= TrackingNumbersCollectionChanged;
        }
    }

    private void QueuePresetRefresh(PresetRefreshScope scope)
    {
        _pendingPresetRefreshes |= scope;
        if (!_suppressPresetChangeNotifications && !_isFlushingPresetRefreshes)
        {
            FlushPendingPresetRefreshes();
        }
    }

    private void FlushPendingPresetRefreshes()
    {
        if (_pendingPresetRefreshes == PresetRefreshScope.None || _isFlushingPresetRefreshes)
        {
            return;
        }

        _isFlushingPresetRefreshes = true;
        try
        {
            var scope = _pendingPresetRefreshes;
            _pendingPresetRefreshes = PresetRefreshScope.None;

            if (scope.HasFlag(PresetRefreshScope.Accounts))
            {
                RefreshAccountUsageCounts();
                scope |= _pendingPresetRefreshes;
                _pendingPresetRefreshes = PresetRefreshScope.None;
            }

            if (scope.HasFlag(PresetRefreshScope.Accounts))
            {
                AccountPresetsView.Refresh();
                RefreshOrderAccountPresets();
                OnPropertyChanged(nameof(AccountPresetCount));
                OnPropertyChanged(nameof(AccountsNavLabel));
                OnPropertyChanged(nameof(ShowAccountGroupHeaders));
                RaiseAccountActionCommandState();
            }

            if (scope.HasFlag(PresetRefreshScope.Items))
            {
                PresetsView.Refresh();
                OrderItemPresetsView.Refresh();
                OnPropertyChanged(nameof(ItemPresetCount));
                OnPropertyChanged(nameof(ItemsNavLabel));
                OnPropertyChanged(nameof(ShowItemGroupHeaders));
            }

            RefreshPresetBulkSelectionState(scope);
            RefreshMerchantRoiSettingsState();
        }
        finally
        {
            _isFlushingPresetRefreshes = false;
        }

        if (_pendingPresetRefreshes != PresetRefreshScope.None)
        {
            FlushPendingPresetRefreshes();
        }
    }

    private void OrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Order order in e.OldItems)
            {
                UnsubscribeFromOrder(order);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (Order order in e.NewItems)
            {
                SubscribeToOrder(order);
            }

            QueueMerchantFaviconFetch(e.NewItems.OfType<Order>().Select(order => order.Merchant));
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var removedOrder in _subscribedTrackingCollections.Keys.Except(Orders).ToList())
            {
                UnsubscribeFromOrder(removedOrder);
            }

            foreach (var order in Orders)
            {
                SubscribeToOrder(order);
            }
        }

        if (_suppressOrderChangeNotifications)
        {
            return;
        }

        RefreshAccountUsageCounts();
        RefreshDashboard();
        RefreshOrderViews();
        RefreshLegacyOrderItemMigrationState();
        RefreshArchiveState();
        RefreshBulkSelectionState();
        PersistIfNeeded();
    }

    private void AccountPresetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AccountPreset preset in e.OldItems)
            {
                preset.PropertyChanged -= AccountPresetPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AccountPreset preset in e.NewItems)
            {
                preset.PropertyChanged += AccountPresetPropertyChanged;
            }
        }

        if (_suppressPresetChangeNotifications)
        {
            _pendingPresetRefreshes |= PresetRefreshScope.Accounts;
            return;
        }

        QueuePresetRefresh(PresetRefreshScope.Accounts);
    }

    private void ItemPresetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ItemPreset preset in e.OldItems)
            {
                preset.PropertyChanged -= ItemPresetPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ItemPreset preset in e.NewItems)
            {
                preset.PropertyChanged += ItemPresetPropertyChanged;
            }
        }

        if (_suppressPresetChangeNotifications)
        {
            _pendingPresetRefreshes |= PresetRefreshScope.Items;
            return;
        }

        QueuePresetRefresh(PresetRefreshScope.Items);
    }

    private void AccountPresetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountPreset.IsSelected))
        {
            if (!_suppressBulkSelectionNotifications && !_suppressPresetChangeNotifications)
            {
                RefreshBulkSelectionState();
            }

            return;
        }

        if (_suppressPresetChangeNotifications)
        {
            _pendingPresetRefreshes |= PresetRefreshScope.Accounts;
            return;
        }

        if (sender is AccountPreset accountPreset &&
            IsAccountPresetEditorOpen &&
            accountPreset.Id == _editingAccountPresetId &&
            e.PropertyName == nameof(AccountPreset.IsFavorite))
        {
            AccountPresetIsFavorite = accountPreset.IsFavorite;
        }

        if (e.PropertyName is nameof(AccountPreset.Email) or nameof(AccountPreset.MerchantHint))
        {
            RaiseAccountActionCommandState();
        }

        QueuePresetRefresh(PresetRefreshScope.Accounts);
        PersistIfNeeded();
    }

    private void ItemPresetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemPreset.IsSelected))
        {
            if (!_suppressBulkSelectionNotifications && !_suppressPresetChangeNotifications)
            {
                RefreshBulkSelectionState();
            }

            return;
        }

        if (_suppressPresetChangeNotifications)
        {
            _pendingPresetRefreshes |= PresetRefreshScope.Items;
            return;
        }

        if (sender is ItemPreset itemPreset &&
            IsPresetEditorOpen &&
            itemPreset.Id == _editingPresetId &&
            e.PropertyName == nameof(ItemPreset.IsFavorite))
        {
            PresetIsFavorite = itemPreset.IsFavorite;
        }

        QueuePresetRefresh(PresetRefreshScope.Items);
        PersistIfNeeded();
    }

    private void OrderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is Order orderWithTrackingCollection && e.PropertyName == nameof(Order.TrackingNumbers))
        {
            AttachTrackingCollection(orderWithTrackingCollection);
        }

        if (e.PropertyName is nameof(Order.TrackingCount)
            or nameof(Order.HasTrackingNumbers)
            or nameof(Order.HasMultipleTrackingNumbers)
            or nameof(Order.TrackingCountSummary)
            or nameof(Order.PrimaryTracking))
        {
            return;
        }

        if (e.PropertyName == nameof(Order.IsItemsExpanded))
        {
            RefreshItemExpansionToggleState();
            return;
        }

        if (e.PropertyName == nameof(Order.IsTrackingExpanded))
        {
            return;
        }

        if (e.PropertyName == nameof(Order.IsSelected))
        {
            if (!_suppressBulkSelectionNotifications && !_suppressOrderChangeNotifications)
            {
                RefreshBulkSelectionState();
            }

            return;
        }

        if (_suppressOrderChangeNotifications)
        {
            return;
        }

        if (sender is Order order && e.PropertyName == nameof(Order.Merchant))
        {
            QueueMerchantFaviconFetch(order.Merchant);
        }

        if (sender is Order orderWithStatus && e.PropertyName == nameof(Order.Status))
        {
            if (_editingOrderId == orderWithStatus.Id)
            {
                FormStatus = orderWithStatus.Status;
            }

            LastActionMessage = $"Order status changed to {EnumDisplayFormatter.Format(orderWithStatus.Status)}.";
        }

        RefreshAccountUsageCounts();
        RefreshDashboard();
        RefreshOrderViews();
        RefreshLegacyOrderItemMigrationState();
        RefreshArchiveState();
        PersistIfNeeded();
    }

    private void TrackingNumbersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressOrderChangeNotifications)
        {
            return;
        }

        RefreshDashboard();
        RefreshOrderViews();
        PersistIfNeeded();
    }

    private void RefreshAfterOrderChange(string? message = null)
    {
        RefreshAccountUsageCounts();
        RefreshOrderViews();
        RefreshDashboard();
        RefreshLegacyOrderItemMigrationState();
        RefreshArchiveState();
        if (!string.IsNullOrWhiteSpace(message))
        {
            LastActionMessage = message;
        }
        PersistIfNeeded();
    }

    private void RefreshOrderViews()
    {
        OrdersView.Refresh();
        ArchivedOrdersView.Refresh();
        OnPropertyChanged(nameof(ShowOrderGroupHeaders));
        RefreshItemExpansionToggleState();
    }

    private void RefreshItemExpansionToggleState()
    {
        OnPropertyChanged(nameof(HasVisibleOrderItems));
        OnPropertyChanged(nameof(VisibleOrderItemsExpansionToggleLabel));
        OnPropertyChanged(nameof(VisibleOrderItemsExpansionToggleToolTip));
        ((RelayCommand)ToggleVisibleOrderItemsExpansionCommand).RaiseCanExecuteChanged();
    }

    private void RefreshArchiveState()
    {
        OnPropertyChanged(nameof(ActiveOrderCount));
        OnPropertyChanged(nameof(OrdersNavLabel));
        OnPropertyChanged(nameof(CompletedOrdersReadyToArchiveCount));
        OnPropertyChanged(nameof(ArchivedOrderCount));
        OnPropertyChanged(nameof(ArchiveNavLabel));
        OnPropertyChanged(nameof(ArchiveCompletedOrdersLabel));
        OnPropertyChanged(nameof(ArchivedOrderSummary));
        ((RelayCommand)ArchiveCompletedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RestoreOrderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ToggleCompletedCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PrimaryOrderActionCommand).RaiseCanExecuteChanged();
        RefreshBulkSelectionState();
    }

    private void RefreshEditorCommandState()
    {
        if (SaveOrderCommand is RelayCommand saveOrderCommand)
        {
            saveOrderCommand.RaiseCanExecuteChanged();
        }

        if (CloseOrderEditorCommand is RelayCommand closeOrderEditorCommand)
        {
            closeOrderEditorCommand.RaiseCanExecuteChanged();
        }

        if (SaveAccountPresetCommand is RelayCommand saveAccountPresetCommand)
        {
            saveAccountPresetCommand.RaiseCanExecuteChanged();
        }

        if (CloseAccountPresetEditorCommand is RelayCommand closeAccountPresetEditorCommand)
        {
            closeAccountPresetEditorCommand.RaiseCanExecuteChanged();
        }

        if (SavePresetCommand is RelayCommand savePresetCommand)
        {
            savePresetCommand.RaiseCanExecuteChanged();
        }

        if (ClosePresetEditorCommand is RelayCommand closePresetEditorCommand)
        {
            closePresetEditorCommand.RaiseCanExecuteChanged();
        }

        RefreshCurrentCommandState();
    }

    private void RefreshCurrentCommandState()
    {
        OnPropertyChanged(nameof(CanSaveCurrent));
        OnPropertyChanged(nameof(CanCloseCurrentPanel));
        ((RelayCommand)NavigateCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NewOrderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ToggleQuickOrderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EditOrderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyOrderAttentionFilterCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NewAccountPresetCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ToggleQuickAccountPresetCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EditAccountPresetCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NewPresetCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ToggleQuickPresetCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EditPresetCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SaveCurrentCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CloseCurrentPanelCommand).RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RaiseAccountActionCommandState()
    {
        if (ViewAccountOrdersCommand is RelayCommand viewAccountOrdersCommand)
        {
            viewAccountOrdersCommand.RaiseCanExecuteChanged();
        }

        if (ClearAccountSessionCommand is RelayCommand clearAccountSessionCommand)
        {
            clearAccountSessionCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshBulkSelectionState()
    {
        OnPropertyChanged(nameof(SelectedActiveOrderCount));
        OnPropertyChanged(nameof(SelectedIncompleteActiveOrderCount));
        OnPropertyChanged(nameof(SelectedCompletedActiveOrderCount));
        OnPropertyChanged(nameof(SelectedArchivedOrderCount));
        OnPropertyChanged(nameof(SelectedAccountPresetCount));
        OnPropertyChanged(nameof(SelectedPresetCount));
        OnPropertyChanged(nameof(HasSelectedActiveOrders));
        OnPropertyChanged(nameof(HasSelectedArchivedOrders));
        OnPropertyChanged(nameof(HasSelectedAccountPresets));
        OnPropertyChanged(nameof(HasSelectedPresets));
        OnPropertyChanged(nameof(AreAllVisibleOrdersSelected));
        OnPropertyChanged(nameof(AreAllVisibleArchivedOrdersSelected));
        OnPropertyChanged(nameof(AreAllVisibleAccountPresetsSelected));
        OnPropertyChanged(nameof(AreAllVisiblePresetsSelected));
        OnPropertyChanged(nameof(ActiveOrderBulkSelectionSummary));
        OnPropertyChanged(nameof(ArchivedOrderBulkSelectionSummary));
        OnPropertyChanged(nameof(AccountPresetBulkSelectionSummary));
        OnPropertyChanged(nameof(PresetBulkSelectionSummary));

        ((RelayCommand)ClearSelectedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MarkSelectedOrdersCompletedCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ArchiveSelectedCompletedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSelectedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearSelectedArchivedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RestoreSelectedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSelectedArchivedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearSelectedAccountPresetsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSelectedAccountPresetsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearSelectedPresetsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteSelectedPresetsCommand).RaiseCanExecuteChanged();
        RefreshSidebarAlerts();
    }

    private void RefreshPresetBulkSelectionState(PresetRefreshScope scope)
    {
        if (scope.HasFlag(PresetRefreshScope.Accounts))
        {
            OnPropertyChanged(nameof(SelectedAccountPresetCount));
            OnPropertyChanged(nameof(HasSelectedAccountPresets));
            OnPropertyChanged(nameof(AreAllVisibleAccountPresetsSelected));
            OnPropertyChanged(nameof(AccountPresetBulkSelectionSummary));
            ((RelayCommand)ClearSelectedAccountPresetsCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteSelectedAccountPresetsCommand).RaiseCanExecuteChanged();
        }

        if (scope.HasFlag(PresetRefreshScope.Items))
        {
            OnPropertyChanged(nameof(SelectedPresetCount));
            OnPropertyChanged(nameof(HasSelectedPresets));
            OnPropertyChanged(nameof(AreAllVisiblePresetsSelected));
            OnPropertyChanged(nameof(PresetBulkSelectionSummary));
            ((RelayCommand)ClearSelectedPresetsCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteSelectedPresetsCommand).RaiseCanExecuteChanged();
        }
    }

    private void RefreshLegacyOrderItemMigrationState()
    {
        OnPropertyChanged(nameof(LegacyOrderItemMigrationCount));
        OnPropertyChanged(nameof(LegacyOrderItemMigrationStatus));
        ((RelayCommand)MigrateLegacyOrderItemsCommand).RaiseCanExecuteChanged();
        RefreshSidebarAlerts();
    }

    private void PersistIfNeeded()
    {
        if (Settings.AutoSave)
        {
            RequestAutosave();
        }
    }

    private void RequestAutosave()
    {
        if (_isDisposed)
        {
            return;
        }

        SettingsSaveStatus = "Saving...";
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Tick -= AutosaveTimerTick;
        _sidebarClockTimer.Stop();
        _sidebarClockTimer.Tick -= SidebarClockTimerTick;
        FormItems.CollectionChanged -= FormItemsCollectionChanged;
        foreach (var item in FormItems)
        {
            item.PropertyChanged -= FormItemPropertyChanged;
        }

        Orders.CollectionChanged -= OrdersCollectionChanged;
        foreach (var order in _subscribedTrackingCollections.Keys.ToList())
        {
            UnsubscribeFromOrder(order);
        }

        AccountPresets.CollectionChanged -= AccountPresetsCollectionChanged;
        foreach (var preset in AccountPresets)
        {
            preset.PropertyChanged -= AccountPresetPropertyChanged;
        }

        ItemPresets.CollectionChanged -= ItemPresetsCollectionChanged;
        foreach (var preset in ItemPresets)
        {
            preset.PropertyChanged -= ItemPresetPropertyChanged;
        }

        Settings.PropertyChanged -= SettingsPropertyChanged;
        Settings.Columns.PropertyChanged -= SettingsPropertyChanged;
        Settings.MerchantProjectedRoiPercents.CollectionChanged -= MerchantProjectedRoiPercentsCollectionChanged;
        foreach (var setting in Settings.MerchantProjectedRoiPercents)
        {
            setting.PropertyChanged -= MerchantProjectedRoiSettingPropertyChanged;
        }
    }
}
