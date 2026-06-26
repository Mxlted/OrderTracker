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

public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan SidebarClockDisplayOffset = TimeSpan.FromSeconds(1);

    private readonly AppDataStore _dataStore = new();
    private readonly BrowserLauncher _browserLauncher = new();
    private readonly DiscordWebhookService _discordWebhookService = new();
    private readonly NetworkTimeService _networkTimeService = new();
    private readonly AppData _data;
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
    private bool _isConfirmationOpen;
    private bool _confirmationIsDanger;
    private string _confirmationTitle = string.Empty;
    private string _confirmationMessage = string.Empty;
    private string _confirmationConfirmText = "Confirm";
    private string _confirmationCancelText = "Cancel";
    private Action? _pendingConfirmationAction;
    private string? _pendingConfirmationCancelMessage;

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
        OrdersView = new ListCollectionView(Orders);
        OrdersView.Filter = FilterOrder;
        ArchivedOrdersView = new ListCollectionView(Orders);
        ArchivedOrdersView.Filter = FilterArchivedOrder;
        AccountPresetsView = CollectionViewSource.GetDefaultView(AccountPresets);
        AccountPresetsView.Filter = FilterAccountPreset;
        PresetsView = CollectionViewSource.GetDefaultView(ItemPresets);
        PresetsView.Filter = FilterPreset;

        Orders.CollectionChanged += OrdersCollectionChanged;
        foreach (var order in Orders)
        {
            order.PropertyChanged += OrderPropertyChanged;
            order.TrackingNumbers.CollectionChanged += TrackingNumbersCollectionChanged;
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

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter?.ToString()));
        NewOrderCommand = new RelayCommand(_ => BeginNewOrder());
        ToggleQuickOrderCommand = new RelayCommand(_ => ToggleQuickOrder());
        EditOrderCommand = new RelayCommand(parameter => BeginEditOrder(parameter as Order), parameter => parameter is Order);
        SaveOrderCommand = new RelayCommand(_ => SaveOrder(), _ => CanSaveOrder);
        CloseOrderEditorCommand = new RelayCommand(_ => CloseOrderEditor(), _ => IsOrderEditorOpen);
        DeleteOrderCommand = new RelayCommand(parameter => DeleteOrder(parameter as Order), parameter => parameter is Order);
        DuplicateOrderCommand = new RelayCommand(parameter => DuplicateOrder(parameter as Order), parameter => parameter is Order);
        ToggleCompletedCommand = new RelayCommand(parameter => ToggleCompleted(parameter as Order), parameter => parameter is Order);
        ArchiveCompletedOrdersCommand = new RelayCommand(_ => ArchiveCompletedOrders(), _ => CompletedOrdersReadyToArchiveCount > 0);
        RestoreOrderCommand = new RelayCommand(parameter => RestoreOrder(parameter as Order), parameter => parameter is Order { IsArchived: true });
        SelectVisibleOrdersCommand = new RelayCommand(_ => SelectOrders(OrdersView, true));
        ClearSelectedOrdersCommand = new RelayCommand(_ => ClearOrderSelection(includeArchived: false), _ => SelectedActiveOrderCount > 0);
        MarkSelectedOrdersCompletedCommand = new RelayCommand(_ => MarkSelectedOrdersCompleted(), _ => SelectedIncompleteActiveOrderCount > 0);
        ArchiveSelectedCompletedOrdersCommand = new RelayCommand(_ => ArchiveSelectedCompletedOrders(), _ => SelectedCompletedActiveOrderCount > 0);
        DeleteSelectedOrdersCommand = new RelayCommand(_ => DeleteSelectedOrders(includeArchived: false), _ => SelectedActiveOrderCount > 0);
        ToggleVisibleOrderItemsExpansionCommand = new RelayCommand(_ => ToggleVisibleOrderItemsExpansion(), _ => HasVisibleOrderItems);
        SelectVisibleArchivedOrdersCommand = new RelayCommand(_ => SelectOrders(ArchivedOrdersView, true));
        ClearSelectedArchivedOrdersCommand = new RelayCommand(_ => ClearOrderSelection(includeArchived: true), _ => SelectedArchivedOrderCount > 0);
        RestoreSelectedOrdersCommand = new RelayCommand(_ => RestoreSelectedOrders(), _ => SelectedArchivedOrderCount > 0);
        DeleteSelectedArchivedOrdersCommand = new RelayCommand(_ => DeleteSelectedOrders(includeArchived: true), _ => SelectedArchivedOrderCount > 0);
        OpenOrderLinkCommand = new RelayCommand(parameter => OpenOrderLink(parameter as Order), parameter => parameter is Order);
        OpenTrackingCommand = new RelayCommand(parameter => OpenTracking(parameter as TrackingEntry), parameter => parameter is TrackingEntry);
        CopyTrackingNumbersCommand = new RelayCommand(parameter => CopyTrackingNumbers(parameter as Order), parameter => parameter is Order);
        AddOrderItemCommand = new RelayCommand(_ => AddFormItem());
        RemoveOrderItemCommand = new RelayCommand(parameter => RemoveFormItem(parameter as OrderItem), parameter => parameter is OrderItem && FormItems.Count > 1);
        SaveSettingsCommand = new RelayCommand(_ => SaveNow("Settings saved."));
        MigrateLegacyOrderItemsCommand = new RelayCommand(_ => MigrateLegacyOrderItems(), _ => LegacyOrderItemMigrationCount > 0);
        SendDiscordStatsCommand = new RelayCommand(async _ => await SendDiscordStatsAsync(), _ => !IsBusy);
        ConfirmDialogCommand = new RelayCommand(_ => ConfirmDialog(), _ => IsConfirmationOpen);
        CancelDialogCommand = new RelayCommand(_ => CancelDialog(), _ => IsConfirmationOpen);

        NewAccountPresetCommand = new RelayCommand(_ => BeginNewAccountPreset());
        ToggleQuickAccountPresetCommand = new RelayCommand(_ => ToggleQuickAccountPreset());
        EditAccountPresetCommand = new RelayCommand(parameter => BeginEditAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset);
        SaveAccountPresetCommand = new RelayCommand(_ => SaveAccountPreset(), _ => CanSaveAccountPreset);
        CloseAccountPresetEditorCommand = new RelayCommand(_ => CloseAccountPresetEditor(), _ => IsAccountPresetEditorOpen);
        DeleteAccountPresetCommand = new RelayCommand(parameter => DeleteAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset);
        DuplicateAccountPresetCommand = new RelayCommand(parameter => DuplicateAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset);
        ApplyAccountPresetCommand = new RelayCommand(parameter => ApplyAccountPreset(parameter as AccountPreset), parameter => parameter is AccountPreset);
        ViewAccountOrdersCommand = new RelayCommand(parameter => ViewAccountOrders(parameter as AccountPreset), parameter => parameter is AccountPreset);
        SelectVisibleAccountPresetsCommand = new RelayCommand(_ => SelectAccountPresets(AccountPresetsView, true));
        ClearSelectedAccountPresetsCommand = new RelayCommand(_ => ClearAccountPresetSelection(), _ => SelectedAccountPresetCount > 0);
        DeleteSelectedAccountPresetsCommand = new RelayCommand(_ => DeleteSelectedAccountPresets(), _ => SelectedAccountPresetCount > 0);

        NewPresetCommand = new RelayCommand(_ => BeginNewPreset());
        ToggleQuickPresetCommand = new RelayCommand(_ => ToggleQuickPreset());
        EditPresetCommand = new RelayCommand(parameter => BeginEditPreset(parameter as ItemPreset), parameter => parameter is ItemPreset);
        SavePresetCommand = new RelayCommand(_ => SavePreset(), _ => CanSavePreset);
        ClosePresetEditorCommand = new RelayCommand(_ => ClosePresetEditor(), _ => IsPresetEditorOpen);
        DeletePresetCommand = new RelayCommand(parameter => DeletePreset(parameter as ItemPreset), parameter => parameter is ItemPreset);
        DuplicatePresetCommand = new RelayCommand(parameter => DuplicatePreset(parameter as ItemPreset), parameter => parameter is ItemPreset);
        ApplyPresetCommand = new RelayCommand(parameter => ApplyPreset(parameter as ItemPreset), parameter => parameter is ItemPreset);
        SelectVisiblePresetsCommand = new RelayCommand(_ => SelectItemPresets(PresetsView, true));
        ClearSelectedPresetsCommand = new RelayCommand(_ => ClearItemPresetSelection(), _ => SelectedPresetCount > 0);
        DeleteSelectedPresetsCommand = new RelayCommand(_ => DeleteSelectedPresets(), _ => SelectedPresetCount > 0);

        FormItems.CollectionChanged += FormItemsCollectionChanged;
        ResetOrderForm();
        ResetAccountPresetForm();
        ResetPresetForm();
        ApplySortAndGroup();
        ApplyArchiveSort();
        RefreshDashboard();
        RefreshArchiveState();
        StartSidebarClock();
        _ = SyncSidebarClockAsync();
    }

    public AppSettings Settings => _data.Settings;

    public ObservableCollection<Order> Orders => _data.Orders;

    public ObservableCollection<AccountPreset> AccountPresets => _data.AccountPresets;

    public ObservableCollection<ItemPreset> ItemPresets => _data.ItemPresets;

    public ICollectionView OrdersView { get; }

    public ICollectionView ArchivedOrdersView { get; }

    public ICollectionView AccountPresetsView { get; }

    public ICollectionView PresetsView { get; }

    public ObservableCollection<MetricCard> MetricCards { get; } = new();

    public ObservableCollection<ChartPoint> MonthlySpend { get; } = new();

    public ObservableCollection<ChartPoint> YearlySpend { get; } = new();

    public ObservableCollection<ChartPoint> MerchantSpend { get; } = new();

    public ObservableCollection<ChartPoint> StatusBreakdown { get; } = new();

    public ObservableCollection<SidebarPanelItem> SidebarAlerts { get; } = new();

    public ObservableCollection<OrderItem> FormItems { get; } = new();

    public Array Pages => Enum.GetValues<AppPage>();

    public IReadOnlyList<MerchantKind> Merchants => AppSettings.ListedMerchants;

    public Array OrderStatuses => Enum.GetValues<OrderStatus>();

    public Array GroupOptions => Enum.GetValues<OrderGroupOption>();

    public Array SortOptions => Enum.GetValues<OrderSortOption>();

    public Array BrowserOptions => Enum.GetValues<BrowserPreference>();

    public Array Themes => Enum.GetValues<AppTheme>();

    public ICommand NavigateCommand { get; }

    public ICommand NewOrderCommand { get; }

    public ICommand ToggleQuickOrderCommand { get; }

    public ICommand EditOrderCommand { get; }

    public ICommand SaveOrderCommand { get; }

    public ICommand CloseOrderEditorCommand { get; }

    public ICommand DeleteOrderCommand { get; }

    public ICommand DuplicateOrderCommand { get; }

    public ICommand ToggleCompletedCommand { get; }

    public ICommand ArchiveCompletedOrdersCommand { get; }

    public ICommand RestoreOrderCommand { get; }

    public ICommand SelectVisibleOrdersCommand { get; }

    public ICommand ClearSelectedOrdersCommand { get; }

    public ICommand MarkSelectedOrdersCompletedCommand { get; }

    public ICommand ArchiveSelectedCompletedOrdersCommand { get; }

    public ICommand DeleteSelectedOrdersCommand { get; }

    public ICommand ToggleVisibleOrderItemsExpansionCommand { get; }

    public ICommand SelectVisibleArchivedOrdersCommand { get; }

    public ICommand ClearSelectedArchivedOrdersCommand { get; }

    public ICommand RestoreSelectedOrdersCommand { get; }

    public ICommand DeleteSelectedArchivedOrdersCommand { get; }

    public ICommand OpenOrderLinkCommand { get; }

    public ICommand OpenTrackingCommand { get; }

    public ICommand CopyTrackingNumbersCommand { get; }

    public ICommand AddOrderItemCommand { get; }

    public ICommand RemoveOrderItemCommand { get; }

    public ICommand SaveSettingsCommand { get; }

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

    public ICommand SelectVisibleAccountPresetsCommand { get; }

    public ICommand ClearSelectedAccountPresetsCommand { get; }

    public ICommand DeleteSelectedAccountPresetsCommand { get; }

    public ICommand NewPresetCommand { get; }

    public ICommand ToggleQuickPresetCommand { get; }

    public ICommand EditPresetCommand { get; }

    public ICommand SavePresetCommand { get; }

    public ICommand ClosePresetEditorCommand { get; }

    public ICommand DeletePresetCommand { get; }

    public ICommand DuplicatePresetCommand { get; }

    public ICommand ApplyPresetCommand { get; }

    public ICommand SelectVisiblePresetsCommand { get; }

    public ICommand ClearSelectedPresetsCommand { get; }

    public ICommand DeleteSelectedPresetsCommand { get; }

    public AppPage SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
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

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OrdersView.Refresh();
                RefreshItemExpansionToggleState();
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
            }
        }
    }

    public OrderSortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                ApplySortAndGroup();
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
            }
        }
    }

    public int CompletedOrdersReadyToArchiveCount => Orders.Count(order => order.Status == OrderStatus.Delivered && !order.IsArchived);

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

    public int SelectedIncompleteActiveOrderCount => Orders.Count(order => order.IsSelected && !order.IsArchived && order.Status != OrderStatus.Delivered);

    public int SelectedCompletedActiveOrderCount => Orders.Count(order => order.IsSelected && !order.IsArchived && order.Status == OrderStatus.Delivered);

    public int SelectedArchivedOrderCount => Orders.Count(order => order.IsSelected && order.IsArchived);

    public int SelectedAccountPresetCount => AccountPresets.Count(preset => preset.IsSelected);

    public int SelectedPresetCount => ItemPresets.Count(preset => preset.IsSelected);

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

    public string PresetBulkSelectionSummary => FormatSelectedCount(SelectedPresetCount, "preset");

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

    public bool CanSaveOrder => IsOrderEditorOpen && FormItems.Any(item => !string.IsNullOrWhiteSpace(item.Name));

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
        set => SetProperty(ref _formMerchant, value);
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
        set => SetProperty(ref _formShippingCostInput, value ?? string.Empty);
    }

    public string FormTaxInput
    {
        get => _formTaxInput;
        set => SetProperty(ref _formTaxInput, value ?? string.Empty);
    }

    public string FormOtherCostInput
    {
        get => _formOtherCostInput;
        set => SetProperty(ref _formOtherCostInput, value ?? string.Empty);
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
        set => SetProperty(ref _formProjectedRoiPercentInput, value ?? string.Empty);
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

    public string PresetEditorTitle => IsEditingPreset ? "Edit preset" : "New preset";

    public bool CanSavePreset => IsPresetEditorOpen && !string.IsNullOrWhiteSpace(PresetName);

    public string PresetName
    {
        get => _presetName;
        set
        {
            if (SetProperty(ref _presetName, value ?? string.Empty))
            {
                ((RelayCommand)SavePresetCommand).RaiseCanExecuteChanged();
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
        set => SetProperty(ref _selectedAccountPreset, value);
    }

    public string AccountPresetSearchText
    {
        get => _accountPresetSearchText;
        set
        {
            if (SetProperty(ref _accountPresetSearchText, value ?? string.Empty))
            {
                AccountPresetsView.Refresh();
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

    public bool CanSaveAccountPreset => IsAccountPresetEditorOpen && !string.IsNullOrWhiteSpace(AccountPresetEmail);

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

    public string DataFilePath => _dataStore.DataFilePath;

    public int LegacyOrderItemMigrationCount => Orders.Count(NeedsLegacyItemMigration);

    public string LegacyOrderItemMigrationStatus => LegacyOrderItemMigrationCount == 0
        ? "Order config is up to date."
        : $"{LegacyOrderItemMigrationCount} order(s) need config update.";

    public void SaveNow(string? message = null)
    {
        try
        {
            _dataStore.Save(_data);
            if (!string.IsNullOrWhiteSpace(message))
            {
                LastActionMessage = message;
            }
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Could not save data: {ex.Message}";
        }
    }

    public void CaptureBrowserLinkWindowPlacement()
    {
        _browserLauncher.CaptureTrackedLinkWindowBounds(Settings);
    }

    private void Navigate(string? page)
    {
        if (Enum.TryParse<AppPage>(page, out var parsed))
        {
            SelectedPage = parsed;
        }
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
        if (IsOrderEditorOpen)
        {
            CloseOrderEditor();
            LastActionMessage = "Order panel closed.";
            return;
        }

        BeginNewOrder();
    }

    private void ResetOrderForm()
    {
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
        FormOrderDate = DateTime.Today;
        FormExpectedDate = null;
        FormDeliveredDate = null;
        FormStatus = OrderStatus.Ordered;
        FormTrackingStatus = string.Empty;
        FormTrackingNumbersText = string.Empty;
        FormNotes = string.Empty;
        OnPropertyChanged(nameof(IsEditingOrder));
        OnPropertyChanged(nameof(OrderEditorTitle));
    }

    private void CloseOrderEditor()
    {
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
        FormOrderDate = order.OrderDate;
        FormExpectedDate = order.ExpectedDate;
        FormDeliveredDate = order.DeliveredDate;
        FormStatus = order.Status;
        FormTrackingStatus = order.TrackingStatus;
        FormTrackingNumbersText = string.Join(Environment.NewLine, order.TrackingNumbers.Select(tracking => tracking.Number));
        FormNotes = order.Notes;
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

        var order = Orders.FirstOrDefault(candidate => candidate.Id == _editingOrderId);
        var isNew = order is null;
        order ??= new Order();

        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            UpdateOrderFromForm(order, items);
            CarrierRecognizer.ApplyRecognition(order);
        });

        if (isNew)
        {
            Orders.Add(order);
        }

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
        order.OrderDate = FormOrderDate ?? DateTime.Today;
        order.ExpectedDate = FormExpectedDate;
        order.DeliveredDate = FormDeliveredDate;
        order.Status = FormStatus;
        order.TrackingStatus = FormTrackingStatus.Trim();
        order.Notes = FormNotes.Trim();

        order.TrackingNumbers.CollectionChanged -= TrackingNumbersCollectionChanged;
        try
        {
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
        finally
        {
            order.TrackingNumbers.CollectionChanged += TrackingNumbersCollectionChanged;
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
            !TryParseMoney(FormOtherCostInput, "other cost", out var otherCost) ||
            !TryParseOptionalPercent(FormProjectedRoiPercentInput, "ROI", out var projectedRoiPercentOverride))
        {
            return false;
        }

        FormShippingCost = shipping;
        FormTax = tax;
        FormOtherCost = otherCost;
        FormProjectedRoiPercentOverride = projectedRoiPercentOverride;
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
                RemoveOrder(order);
                RefreshAfterOrderChange($"Deleted {label}.");
            },
            isDanger: true,
            cancelMessage: "Delete canceled.");
    }

    private void RemoveOrder(Order order)
    {
        order.PropertyChanged -= OrderPropertyChanged;
        order.TrackingNumbers.CollectionChanged -= TrackingNumbersCollectionChanged;
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

        var copy = new Order
        {
            AccountEmail = order.AccountEmail,
            Merchant = order.Merchant,
            Items = new ObservableCollection<OrderItem>(GetOrderItems(order).Select(item => item.Clone())),
            ShippingCost = order.ShippingCost,
            Tax = order.Tax,
            OtherCost = order.OtherCost,
            ProjectedRoiPercentOverride = order.ProjectedRoiPercentOverride,
            OrderDate = DateTime.Today,
            ExpectedDate = null,
            DeliveredDate = null,
            Status = OrderStatus.Ordered,
            Notes = string.IsNullOrWhiteSpace(order.OrderNumber)
                ? "Duplicated order."
                : $"Duplicated from {order.OrderNumber.Trim()}."
        };

        Orders.Add(copy);
        BeginEditOrder(copy);
        RefreshAfterOrderChange("Duplicated order. Add the new order number and tracking when ready.");
    }

    private void ToggleCompleted(Order? order)
    {
        if (order is null)
        {
            return;
        }

        var message = "Order updated.";
        RunWithOrderChangeNotificationsSuppressed(() =>
        {
            if (order.Status == OrderStatus.Delivered)
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
            .Where(order => order.Status == OrderStatus.Delivered && !order.IsArchived)
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
        var message = HideCompleted && order.Status == OrderStatus.Delivered
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

    private void SelectOrders(System.Collections.IEnumerable orders, bool selected)
    {
        foreach (var value in orders)
        {
            if (value is Order order)
            {
                order.IsSelected = selected;
            }
        }

        RefreshBulkSelectionState();
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
        foreach (var order in Orders.Where(order => order.IsArchived == includeArchived && order.IsSelected))
        {
            order.IsSelected = false;
        }

        RefreshBulkSelectionState();
    }

    private void MarkSelectedOrdersCompleted()
    {
        var candidates = Orders
            .Where(order => order.IsSelected && !order.IsArchived && order.Status != OrderStatus.Delivered)
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
            .Where(order => order.IsSelected && !order.IsArchived && order.Status == OrderStatus.Delivered)
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
        var message = HideCompleted && candidates.Any(order => order.Status == OrderStatus.Delivered)
            ? $"Restored {candidates.Count} {noun}. Turn off Hide completed to view completed orders."
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
                foreach (var order in candidates)
                {
                    RemoveOrder(order);
                }

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

        ApplyRecognitionQuietly(order);
        var url = CarrierRecognizer.BuildOrderUrl(order);
        LastActionMessage = _browserLauncher.OpenUrl(url, Settings, BuildBrowserSessionContext(order, url));
        RefreshAfterOrderChange();
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

        ApplyRecognitionQuietly(order);
        var url = CarrierRecognizer.BuildTrackingUrl(order, tracking);
        LastActionMessage = _browserLauncher.OpenUrl(url, Settings, BuildBrowserSessionContext(order, url));
        RefreshAfterOrderChange();
    }

    private BrowserSessionContext? BuildBrowserSessionContext(Order order, string url)
    {
        if (!Settings.UseAccountBrowserSessions ||
            order.Merchant != MerchantKind.Amazon ||
            string.IsNullOrWhiteSpace(order.AccountEmail) ||
            !IsAmazonUrl(url))
        {
            return null;
        }

        var accountEmail = order.AccountEmail.Trim();
        var preset = AccountPresets.FirstOrDefault(candidate =>
            string.Equals(candidate.Email.Trim(), accountEmail, StringComparison.OrdinalIgnoreCase));

        return new BrowserSessionContext
        {
            Merchant = MerchantKind.Amazon,
            AccountKey = accountEmail,
            AccountDisplayName = preset?.DisplayName ?? accountEmail
        };
    }

    private BrowserSessionContext? BuildBrowserSessionContext(AccountPreset preset, MerchantKind merchant, string url)
    {
        if (!Settings.UseAccountBrowserSessions ||
            merchant != MerchantKind.Amazon ||
            string.IsNullOrWhiteSpace(preset.Email) ||
            !IsAmazonUrl(url))
        {
            return null;
        }

        return new BrowserSessionContext
        {
            Merchant = MerchantKind.Amazon,
            AccountKey = preset.Email.Trim(),
            AccountDisplayName = preset.DisplayName
        };
    }

    private static bool IsAmazonUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.Equals("amazon.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".amazon.com", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyRecognitionQuietly(Order order)
    {
        RunWithOrderChangeNotificationsSuppressed(() => CarrierRecognizer.ApplyRecognition(order));
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
    }

    private static IEnumerable<string> ParseTrackingNumbers(string text)
    {
        return (text ?? string.Empty)
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CarrierRecognizer.NormalizeTrackingNumber)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
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

        if (decimal.TryParse(input, NumberStyles.Currency, CultureInfo.CurrentCulture, out value) ||
            decimal.TryParse(input, NumberStyles.Currency, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        LastActionMessage = $"Enter a valid {label}.";
        value = 0m;
        return false;
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

    private static string FormatPercentInput(decimal? value)
    {
        return value.HasValue
            ? Math.Max(0m, value.Value).ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    private void CopyTrackingNumbers(Order? order)
    {
        if (order is null)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, order.TrackingNumbers.Select(tracking => tracking.Number).Where(number => !string.IsNullOrWhiteSpace(number)));
        if (string.IsNullOrWhiteSpace(text))
        {
            LastActionMessage = "That order has no tracking numbers to copy.";
            return;
        }

        try
        {
            Clipboard.SetText(text);
            LastActionMessage = "Tracking numbers copied to clipboard.";
        }
        catch (Exception ex)
        {
            LastActionMessage = $"Could not copy tracking numbers: {ex.Message}";
        }
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
    }

    private void FormItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderItem.Name))
        {
            ((RelayCommand)SaveOrderCommand).RaiseCanExecuteChanged();
        }
    }

    private void ApplyAccountPreset(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (!IsOrderEditorOpen)
        {
            BeginNewOrder();
        }

        FormAccountEmail = preset.Email;
        if (preset.MerchantHint != MerchantKind.Unknown)
        {
            FormMerchant = preset.MerchantHint;
        }

        RunWithPresetChangeNotificationsSuppressed(() => preset.UsageCount++);
        SelectedPage = AppPage.Orders;
        LastActionMessage = $"Applied account '{preset.DisplayName}'.";
        PersistIfNeeded();
    }

    private void ViewAccountOrders(AccountPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        var url = CarrierRecognizer.BuildAmazonOrderHistoryUrl();
        LastActionMessage = _browserLauncher.OpenUrl(url, Settings, BuildBrowserSessionContext(preset, MerchantKind.Amazon, url));
    }

    private void ApplyPreset(ItemPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (!IsOrderEditorOpen)
        {
            BeginNewOrder();
        }

        var target = FormItems.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Name));

        if (target is null)
        {
            target = new OrderItem();
            AddFormItem(target);
        }

        target.Name = preset.Name;
        target.Quantity = preset.DefaultQuantity;
        target.UnitPrice = preset.DefaultUnitPrice;
        if (FormShippingCost == 0m)
        {
            FormShippingCost = preset.DefaultShipping;
        }

        if (FormTax == 0m)
        {
            FormTax = preset.DefaultTax;
        }

        if (preset.MerchantHint != MerchantKind.Unknown)
        {
            FormMerchant = preset.MerchantHint;
        }

        RunWithPresetChangeNotificationsSuppressed(() => preset.UsageCount++);
        SelectedPage = AppPage.Orders;
        LastActionMessage = $"Applied preset '{preset.Name}'.";
        PersistIfNeeded();
    }

    private void BeginNewAccountPreset()
    {
        ResetAccountPresetForm();
        IsAccountPresetEditorOpen = true;
        SelectedPage = AppPage.Accounts;
    }

    private void ToggleQuickAccountPreset()
    {
        if (IsAccountPresetEditorOpen)
        {
            CloseAccountPresetEditor();
            LastActionMessage = "Account panel closed.";
            return;
        }

        BeginNewAccountPreset();
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
        });

        if (isNew)
        {
            AccountPresets.Add(preset);
        }

        SelectedAccountPreset = preset;
        AccountPresetsView.Refresh();
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
                AccountPresets.Remove(preset);
                if (SelectedAccountPreset == preset)
                {
                    SelectedAccountPreset = null;
                }

                if (_editingAccountPresetId == preset.Id)
                {
                    CloseAccountPresetEditor();
                }

                AccountPresetsView.Refresh();
                RefreshBulkSelectionState();
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

        var copy = new AccountPreset
        {
            Name = $"{preset.DisplayName} copy".Trim(),
            Email = preset.Email,
            MerchantHint = preset.MerchantHint,
            IsFavorite = preset.IsFavorite,
            Notes = preset.Notes
        };

        AccountPresets.Add(copy);
        SelectedAccountPreset = copy;
        AccountPresetsView.Refresh();
        SaveNow("Account preset duplicated.");
        BeginEditAccountPreset(copy);
    }

    private void SelectAccountPresets(System.Collections.IEnumerable presets, bool selected)
    {
        foreach (var value in presets)
        {
            if (value is AccountPreset preset)
            {
                preset.IsSelected = selected;
            }
        }

        RefreshBulkSelectionState();
    }

    private void ClearAccountPresetSelection()
    {
        foreach (var preset in AccountPresets.Where(preset => preset.IsSelected))
        {
            preset.IsSelected = false;
        }

        RefreshBulkSelectionState();
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
                foreach (var preset in candidates)
                {
                    AccountPresets.Remove(preset);
                }

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

                AccountPresetsView.Refresh();
                RefreshBulkSelectionState();
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

    private void ToggleQuickPreset()
    {
        if (IsPresetEditorOpen)
        {
            ClosePresetEditor();
            LastActionMessage = "Preset panel closed.";
            return;
        }

        BeginNewPreset();
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
        });

        if (isNew)
        {
            ItemPresets.Add(preset);
        }

        SelectedPreset = preset;
        PresetsView.Refresh();
        SaveNow($"Preset {(isNew ? "added" : "updated")}.");
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
            "Delete item preset",
            $"Delete {label}? This permanently removes the saved item shortcut.",
            "Delete",
            () =>
            {
                ItemPresets.Remove(preset);
                if (SelectedPreset == preset)
                {
                    SelectedPreset = null;
                }

                if (_editingPresetId == preset.Id)
                {
                    ClosePresetEditor();
                }

                PresetsView.Refresh();
                RefreshBulkSelectionState();
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

        ItemPresets.Add(copy);
        SelectedPreset = copy;
        PresetsView.Refresh();
        SaveNow("Preset duplicated.");
        BeginEditPreset(copy);
    }

    private void SelectItemPresets(System.Collections.IEnumerable presets, bool selected)
    {
        foreach (var value in presets)
        {
            if (value is ItemPreset preset)
            {
                preset.IsSelected = selected;
            }
        }

        RefreshBulkSelectionState();
    }

    private void ClearItemPresetSelection()
    {
        foreach (var preset in ItemPresets.Where(preset => preset.IsSelected))
        {
            preset.IsSelected = false;
        }

        RefreshBulkSelectionState();
    }

    private void DeleteSelectedPresets()
    {
        var candidates = ItemPresets
            .Where(preset => preset.IsSelected)
            .ToList();

        if (candidates.Count == 0)
        {
            LastActionMessage = "No presets are selected.";
            RefreshBulkSelectionState();
            return;
        }

        var noun = candidates.Count == 1 ? "preset" : "presets";
        ShowConfirmation(
            "Delete selected presets",
            $"Delete {candidates.Count} selected {noun}? This permanently removes the selected item presets.{FormatCandidateExamples(candidates, DescribeItemPreset)}",
            "Delete",
            () =>
            {
                foreach (var preset in candidates)
                {
                    ItemPresets.Remove(preset);
                }

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

                PresetsView.Refresh();
                RefreshBulkSelectionState();
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
        catch (Exception ex)
        {
            LastActionMessage = $"Discord send failed: {ex.Message}";
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

        if (HideCompleted && order.Status == OrderStatus.Delivered)
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

    private IEnumerable<SortDescription> GetSortDescriptions()
    {
        return SelectedSort switch
        {
            OrderSortOption.OldestFirst => new[] { new SortDescription(nameof(Order.OrderDate), ListSortDirection.Ascending) },
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

    private void RefreshDashboard()
    {
        MetricCards.Clear();
        foreach (var card in BuildMetricCards())
        {
            MetricCards.Add(card);
        }

        ReplaceChart(MonthlySpend, BuildMonthlySpend());
        ReplaceChart(YearlySpend, BuildYearlySpend());
        ReplaceChart(MerchantSpend, BuildMerchantSpend());
        ReplaceChart(StatusBreakdown, BuildStatusBreakdown());
        RefreshSidebarAlerts();
    }

    private void RefreshSidebarAlerts()
    {
        ReplaceSidebarItems(SidebarAlerts, BuildSidebarAlerts());
    }

    private IEnumerable<SidebarPanelItem> BuildSidebarAlerts()
    {
        var today = DateTime.Today;
        var openOrders = Orders.Where(order => !order.IsArchived && IsOpenOrder(order)).ToList();
        var alerts = new List<SidebarPanelItem>();

        var overdueOrders = openOrders.Count(order => order.ExpectedDate?.Date < today);
        if (overdueOrders > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Past expected",
                Detail = $"{FormatSimpleCount(overdueOrders, "order")} past expected date.",
                Accent = "#E05D5D"
            });
        }

        var dueTodayOrders = openOrders.Count(order => order.ExpectedDate?.Date == today);
        if (dueTodayOrders > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Expected today",
                Detail = $"{FormatSimpleCount(dueTodayOrders, "order")} may land today.",
                Accent = "#FFB547"
            });
        }

        if (CompletedOrdersReadyToArchiveCount > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Ready to archive",
                Detail = $"{FormatSimpleCount(CompletedOrdersReadyToArchiveCount, "order")} can move off Orders.",
                Accent = "#2F9E7E"
            });
        }

        var missingTrackingOrders = openOrders.Count(order => !order.HasTrackingNumbers);
        if (missingTrackingOrders > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Missing tracking",
                Detail = $"{FormatSimpleCount(missingTrackingOrders, "open order")} without tracking.",
                Accent = "#7C9BFF"
            });
        }

        var selectedCount = SelectedActiveOrderCount + SelectedArchivedOrderCount + SelectedAccountPresetCount + SelectedPresetCount;
        if (selectedCount > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Selection active",
                Detail = $"{FormatSimpleCount(selectedCount, "item")} selected across pages.",
                Accent = "#B389FF"
            });
        }

        if (LegacyOrderItemMigrationCount > 0)
        {
            alerts.Add(new SidebarPanelItem
            {
                Label = "Config update",
                Detail = $"{FormatSimpleCount(LegacyOrderItemMigrationCount, "order")} awaiting item migration.",
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

    private IEnumerable<MetricCard> BuildMetricCards()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var yearStart = new DateTime(today.Year, 1, 1);
        var yearEnd = yearStart.AddYears(1);
        var activeOpenOrders = Orders.Where(order => !order.IsArchived && IsOpenOrder(order)).ToList();
        var openOrders = activeOpenOrders.Count;
        var openBalance = activeOpenOrders.Sum(order => order.TotalCost);
        var deliveredThisMonth = Orders.Count(order =>
            order.Status == OrderStatus.Delivered &&
            order.DeliveredDate >= monthStart &&
            order.DeliveredDate < monthEnd);
        var totalSpend = Orders.Sum(order => order.TotalCost);
        var monthOrders = Orders.Where(order => order.OrderDate >= monthStart && order.OrderDate < monthEnd).ToList();
        var yearOrders = Orders.Where(order => order.OrderDate >= yearStart && order.OrderDate < yearEnd).ToList();
        var monthSpend = monthOrders.Sum(order => order.TotalCost);
        var yearSpend = yearOrders.Sum(order => order.TotalCost);
        var projectedMonthRoi = CalculateProjectedRoi(monthOrders);
        var projectedYearRoi = CalculateProjectedRoi(yearOrders);
        var monthEffectiveRoiPercent = CalculateEffectiveRoiPercent(monthSpend, projectedMonthRoi);
        var yearEffectiveRoiPercent = CalculateEffectiveRoiPercent(yearSpend, projectedYearRoi);

        return new[]
        {
            new MetricCard { Label = "Open orders", Value = openOrders.ToString(CultureInfo.CurrentCulture), Detail = "Still moving or waiting", Accent = "#5CC8FF" },
            new MetricCard { Label = "Open balance", Value = openBalance.ToString("C", CultureInfo.CurrentCulture), Detail = "Not yet completed", Accent = "#F57FB0" },
            new MetricCard { Label = "Total spend", Value = totalSpend.ToString("C", CultureInfo.CurrentCulture), Detail = "All tracked orders", Accent = "#7CDB7C" },
            new MetricCard { Label = "This month", Value = monthSpend.ToString("C", CultureInfo.CurrentCulture), Detail = "Orders placed this month", Accent = "#FFB547" },
            new MetricCard { Label = "Projected month ROI", Value = projectedMonthRoi.ToString("C", CultureInfo.CurrentCulture), Detail = $"{FormatPercent(monthEffectiveRoiPercent)} blended merchant rate", Accent = "#2F9E7E" },
            new MetricCard { Label = "Projected year ROI", Value = projectedYearRoi.ToString("C", CultureInfo.CurrentCulture), Detail = $"{FormatPercent(yearEffectiveRoiPercent)} blended merchant rate", Accent = "#7C9BFF" },
            new MetricCard { Label = "Completed", Value = deliveredThisMonth.ToString(CultureInfo.CurrentCulture), Detail = "Delivered this month", Accent = "#B389FF" }
        };
    }

    private decimal CalculateProjectedRoi(IEnumerable<Order> orders)
    {
        return orders.Sum(order => CalculateProjectedRoi(order.TotalCost, Settings.GetProjectedRoiPercent(order)));
    }

    private static decimal CalculateProjectedRoi(decimal spend, decimal percent)
    {
        return spend * Math.Max(0m, percent) / 100m;
    }

    private static decimal CalculateEffectiveRoiPercent(decimal spend, decimal projectedRoi)
    {
        return spend <= 0m ? 0m : projectedRoi / spend * 100m;
    }

    private static string FormatPercent(decimal percent)
    {
        return string.Concat(Math.Max(0m, percent).ToString("0.##", CultureInfo.CurrentCulture), "%");
    }

    private static bool IsOpenOrder(Order order)
    {
        return order.Status is not OrderStatus.Delivered and not OrderStatus.Cancelled and not OrderStatus.Returned;
    }

    private IEnumerable<ChartPoint> BuildMonthlySpend()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-11);
        var accents = new[] { "#5CC8FF", "#7C9BFF", "#B389FF", "#2F9E7E", "#FFB547", "#E05D5D" };
        var points = Enumerable.Range(0, 12)
            .Select(offset => new { Month = start.AddMonths(offset), Accent = accents[offset % accents.Length] })
            .Select(point =>
            {
                var next = point.Month.AddMonths(1);
                var value = Orders.Where(order => order.OrderDate >= point.Month && order.OrderDate < next).Sum(order => order.TotalCost);
                return new ChartPoint
                {
                    Label = point.Month.ToString("MMM yy", CultureInfo.CurrentCulture),
                    Value = value,
                    DisplayValue = value.ToString("C0", CultureInfo.CurrentCulture),
                    Accent = point.Accent
                };
            })
            .ToList();

        ApplyPercents(points);
        return points;
    }

    private IEnumerable<ChartPoint> BuildYearlySpend()
    {
        var points = Orders
            .GroupBy(order => order.OrderDate.Year)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var value = group.Sum(order => order.TotalCost);
                return new ChartPoint
                {
                    Label = group.Key.ToString(CultureInfo.CurrentCulture),
                    Value = value,
                    DisplayValue = value.ToString("C0", CultureInfo.CurrentCulture),
                    Accent = "#7C9BFF"
                };
            })
            .DefaultIfEmpty(new ChartPoint { Label = DateTime.Today.Year.ToString(CultureInfo.CurrentCulture), DisplayValue = 0m.ToString("C0", CultureInfo.CurrentCulture), Accent = "#7C9BFF" })
            .ToList();

        ApplyPercents(points);
        return points;
    }

    private IEnumerable<ChartPoint> BuildMerchantSpend()
    {
        var points = Orders
            .GroupBy(order => order.Merchant)
            .OrderByDescending(group => group.Sum(order => order.TotalCost))
            .Take(8)
            .Select(group =>
            {
                var value = group.Sum(order => order.TotalCost);
                return new ChartPoint
                {
                    Label = group.Key.ToString(),
                    Value = value,
                    DisplayValue = value.ToString("C0", CultureInfo.CurrentCulture),
                    Accent = GetMerchantAccent(group.Key)
                };
            })
            .DefaultIfEmpty(new ChartPoint { Label = "No orders", DisplayValue = 0m.ToString("C0", CultureInfo.CurrentCulture), Accent = "#6B7A90" })
            .ToList();

        ApplyPercents(points);
        return points;
    }

    private IEnumerable<ChartPoint> BuildStatusBreakdown()
    {
        var totalOrders = Orders.Count;
        var points = Enum.GetValues<OrderStatus>()
            .Select(status =>
            {
                var count = Orders.Count(order => order.Status == status);
                return new ChartPoint
                {
                    Label = FormatStatusLabel(status),
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
            point.Percent = max <= 0 ? 0 : Math.Max(4, (double)(point.Value / max * 100));
        }
    }

    private static void ApplyStatusPercents(IList<ChartPoint> points, int totalOrders)
    {
        foreach (var point in points)
        {
            point.Percent = totalOrders <= 0 ? 0 : Math.Max(6, (double)(point.Value / totalOrders * 100));
        }
    }

    private static string FormatStatusLabel(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.OutForDelivery => "Out for delivery",
            _ => status.ToString()
        };
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

    private void StartSidebarClock()
    {
        UpdateSidebarClock();
        _sidebarClockTimer.Interval = TimeSpan.FromSeconds(1);
        _sidebarClockTimer.Tick += (_, _) => UpdateSidebarClock();
        _sidebarClockTimer.Start();
    }

    private async Task SyncSidebarClockAsync()
    {
        var networkUtcTime = await _networkTimeService.TryGetUtcTimeAsync();
        if (networkUtcTime is null)
        {
            return;
        }

        _networkClockUtc = networkUtcTime.Value;
        _networkClockTimestamp = Stopwatch.GetTimestamp();
        UpdateSidebarClock();
    }

    private void UpdateSidebarClock()
    {
        var previousDate = _sidebarDateTime.Date;
        _sidebarDateTime = GetSidebarDateTime();
        OnPropertyChanged(nameof(SidebarDate));
        OnPropertyChanged(nameof(SidebarTime));

        if (_sidebarDateTime.Date != previousDate)
        {
            RefreshSidebarAlerts();
        }
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
            details.Add(order.Merchant.ToString());
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
            details.Add(preset.MerchantHint.ToString());
        }

        return details.Count == 0
            ? label
            : $"{label} ({string.Join(", ", details)})";
    }

    private static string DescribeItemPreset(ItemPreset preset)
    {
        var label = string.IsNullOrWhiteSpace(preset.Name)
            ? "this item preset"
            : $"item preset '{preset.Name.Trim()}'";

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(preset.Category))
        {
            details.Add(preset.Category.Trim());
        }

        if (preset.MerchantHint != MerchantKind.Unknown)
        {
            details.Add(preset.MerchantHint.ToString());
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
            ? $"No {singularName}s selected."
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
        if (sender == Settings &&
            e.PropertyName is nameof(AppSettings.WindowWidth)
                or nameof(AppSettings.WindowHeight)
                or nameof(AppSettings.WindowLeft)
                or nameof(AppSettings.WindowTop)
                or nameof(AppSettings.IsWindowMaximized))
        {
            return;
        }

        if (sender == Settings && e.PropertyName == nameof(AppSettings.AutoSave))
        {
            SaveNow();
            return;
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
        PersistIfNeeded();
    }

    private void OrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Order order in e.OldItems)
            {
                order.PropertyChanged -= OrderPropertyChanged;
                order.TrackingNumbers.CollectionChanged -= TrackingNumbersCollectionChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (Order order in e.NewItems)
            {
                order.PropertyChanged += OrderPropertyChanged;
                order.TrackingNumbers.CollectionChanged += TrackingNumbersCollectionChanged;
            }
        }

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

        RefreshBulkSelectionState();
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

        RefreshBulkSelectionState();
    }

    private void AccountPresetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountPreset.IsSelected))
        {
            RefreshBulkSelectionState();
            return;
        }

        if (_suppressPresetChangeNotifications)
        {
            return;
        }

        if (sender is AccountPreset accountPreset &&
            IsAccountPresetEditorOpen &&
            accountPreset.Id == _editingAccountPresetId &&
            e.PropertyName == nameof(AccountPreset.IsFavorite))
        {
            AccountPresetIsFavorite = accountPreset.IsFavorite;
        }

        AccountPresetsView.Refresh();
        PersistIfNeeded();
    }

    private void ItemPresetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemPreset.IsSelected))
        {
            RefreshBulkSelectionState();
            return;
        }

        if (_suppressPresetChangeNotifications)
        {
            return;
        }

        if (sender is ItemPreset itemPreset &&
            IsPresetEditorOpen &&
            itemPreset.Id == _editingPresetId &&
            e.PropertyName == nameof(ItemPreset.IsFavorite))
        {
            PresetIsFavorite = itemPreset.IsFavorite;
        }

        PresetsView.Refresh();
        PersistIfNeeded();
    }

    private void OrderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
            RefreshBulkSelectionState();
            return;
        }

        if (_suppressOrderChangeNotifications)
        {
            return;
        }

        if (sender is Order order && e.PropertyName == nameof(Order.Status))
        {
            if (_editingOrderId == order.Id)
            {
                FormStatus = order.Status;
            }

            LastActionMessage = $"Order status changed to {order.Status}.";
        }

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
        OnPropertyChanged(nameof(CompletedOrdersReadyToArchiveCount));
        OnPropertyChanged(nameof(ArchivedOrderCount));
        OnPropertyChanged(nameof(ArchiveCompletedOrdersLabel));
        OnPropertyChanged(nameof(ArchivedOrderSummary));
        ((RelayCommand)ArchiveCompletedOrdersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RestoreOrderCommand).RaiseCanExecuteChanged();
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
    }

    private void RefreshBulkSelectionState()
    {
        OnPropertyChanged(nameof(SelectedActiveOrderCount));
        OnPropertyChanged(nameof(SelectedIncompleteActiveOrderCount));
        OnPropertyChanged(nameof(SelectedCompletedActiveOrderCount));
        OnPropertyChanged(nameof(SelectedArchivedOrderCount));
        OnPropertyChanged(nameof(SelectedAccountPresetCount));
        OnPropertyChanged(nameof(SelectedPresetCount));
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
            SaveNow();
        }
    }
}
