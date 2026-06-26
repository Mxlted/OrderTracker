using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class Order : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _accountEmail = string.Empty;
    private MerchantKind _merchant = MerchantKind.Unknown;
    private string _orderNumber = string.Empty;
    private string _orderLink = string.Empty;
    private string _item = string.Empty;
    private int _quantity = 1;
    private decimal _unitPrice;
    private decimal _shippingCost;
    private decimal _tax;
    private decimal _otherCost;
    private decimal? _projectedRoiPercentOverride;
    private ObservableCollection<OrderItem> _items = new();
    private DateTime _orderDate = DateTime.Today;
    private DateTime? _expectedDate;
    private DateTime? _deliveredDate;
    private OrderStatus _status = OrderStatus.Ordered;
    private string _trackingStatus = string.Empty;
    private string _notes = string.Empty;
    private bool _isItemsExpanded;
    private bool _isTrackingExpanded;
    private bool _isArchived;
    private bool _isSelected;
    private ObservableCollection<TrackingEntry> _trackingNumbers = new();

    public Order()
    {
        SubscribeItems(_items);
        SubscribeTrackingNumbers(_trackingNumbers);
    }

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value ?? string.Empty);
    }

    public string AccountEmail
    {
        get => _accountEmail;
        set => SetProperty(ref _accountEmail, value ?? string.Empty);
    }

    public MerchantKind Merchant
    {
        get => _merchant;
        set
        {
            if (SetProperty(ref _merchant, value))
            {
                OnPropertyChanged(nameof(MerchantIcon));
            }
        }
    }

    public string OrderNumber
    {
        get => _orderNumber;
        set => SetProperty(ref _orderNumber, value ?? string.Empty);
    }

    public string OrderLink
    {
        get => _orderLink;
        set => SetProperty(ref _orderLink, value ?? string.Empty);
    }

    public string Item
    {
        get => _item;
        set
        {
            if (SetProperty(ref _item, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ItemCount));
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(ItemCountSummary));
                OnPropertyChanged(nameof(HasMultipleItems));
                OnPropertyChanged(nameof(HasLegacyItemFields));
                OnPropertyChanged(nameof(PrimaryItem));
                OnPropertyChanged(nameof(ItemsSummary));
                OnPropertyChanged(nameof(ItemsToolTip));
            }
        }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, Math.Max(1, value)))
            {
                OnPropertyChanged(nameof(TotalQuantity));
                OnPropertyChanged(nameof(ItemsToolTip));
                OnCostChanged();
            }
        }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (SetProperty(ref _unitPrice, value))
            {
                OnCostChanged();
            }
        }
    }

    public decimal ShippingCost
    {
        get => _shippingCost;
        set
        {
            if (SetProperty(ref _shippingCost, value))
            {
                OnCostChanged();
            }
        }
    }

    public decimal Tax
    {
        get => _tax;
        set
        {
            if (SetProperty(ref _tax, value))
            {
                OnCostChanged();
            }
        }
    }

    public decimal OtherCost
    {
        get => _otherCost;
        set
        {
            if (SetProperty(ref _otherCost, value))
            {
                OnCostChanged();
            }
        }
    }

    public decimal? ProjectedRoiPercentOverride
    {
        get => _projectedRoiPercentOverride;
        set
        {
            var normalized = value.HasValue ? Math.Max(0m, value.Value) : (decimal?)null;
            SetProperty(ref _projectedRoiPercentOverride, normalized);
        }
    }

    public DateTime OrderDate
    {
        get => _orderDate;
        set
        {
            if (SetProperty(ref _orderDate, value))
            {
                OnPropertyChanged(nameof(OrderMonth));
                OnPropertyChanged(nameof(OrderYear));
            }
        }
    }

    public DateTime? ExpectedDate
    {
        get => _expectedDate;
        set
        {
            if (SetProperty(ref _expectedDate, value))
            {
                OnPropertyChanged(nameof(ExpectedSortDate));
            }
        }
    }

    public DateTime? DeliveredDate
    {
        get => _deliveredDate;
        set => SetProperty(ref _deliveredDate, value);
    }

    public OrderStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsCompleted));
            }
        }
    }

    public string TrackingStatus
    {
        get => _trackingStatus;
        set => SetProperty(ref _trackingStatus, value ?? string.Empty);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? string.Empty);
    }

    public bool IsArchived
    {
        get => _isArchived;
        set => SetProperty(ref _isArchived, value);
    }

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ObservableCollection<TrackingEntry> TrackingNumbers
    {
        get => _trackingNumbers;
        set
        {
            if (ReferenceEquals(_trackingNumbers, value))
            {
                return;
            }

            UnsubscribeTrackingNumbers(_trackingNumbers);
            _trackingNumbers = value ?? new ObservableCollection<TrackingEntry>();
            SubscribeTrackingNumbers(_trackingNumbers);
            OnPropertyChanged();
            OnTrackingNumbersChanged();
        }
    }

    public ObservableCollection<OrderItem> Items
    {
        get => _items;
        set
        {
            if (ReferenceEquals(_items, value))
            {
                return;
            }

            UnsubscribeItems(_items);
            _items = value ?? new ObservableCollection<OrderItem>();
            SubscribeItems(_items);
            OnPropertyChanged();
            OnItemsChanged();
        }
    }

    [JsonIgnore]
    public bool IsItemsExpanded
    {
        get => _isItemsExpanded;
        set => SetProperty(ref _isItemsExpanded, value);
    }

    [JsonIgnore]
    public bool IsTrackingExpanded
    {
        get => _isTrackingExpanded;
        set => SetProperty(ref _isTrackingExpanded, value);
    }

    [JsonIgnore]
    public int TrackingCount => TrackingNumbers.Count(tracking => !string.IsNullOrWhiteSpace(tracking.Number));

    [JsonIgnore]
    public bool HasTrackingNumbers => TrackingCount > 0;

    [JsonIgnore]
    public bool HasMultipleTrackingNumbers => TrackingCount > 1;

    [JsonIgnore]
    public string TrackingCountSummary => TrackingCount == 1 ? "1 tracking number" : $"{TrackingCount} tracking numbers";

    [JsonIgnore]
    public TrackingEntry? PrimaryTracking => TrackingNumbers.FirstOrDefault(tracking => !string.IsNullOrWhiteSpace(tracking.Number));

    [JsonIgnore]
    public int ItemCount => Items.Count > 0 ? Items.Count : string.IsNullOrWhiteSpace(Item) ? 0 : 1;

    [JsonIgnore]
    public bool HasItems => ItemCount > 0;

    [JsonIgnore]
    public bool HasMultipleItems => ItemCount > 1;

    [JsonIgnore]
    public string ItemCountSummary => ItemCount == 1 ? "1 item" : $"{ItemCount} items";

    [JsonIgnore]
    public int TotalQuantity => Items.Count > 0 ? Items.Sum(item => item.Quantity) : Quantity;

    [JsonIgnore]
    public string PrimaryItem => Items.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Name))?.Name.Trim() ?? Item;

    [JsonIgnore]
    public string ItemsSummary
    {
        get
        {
            if (Items.Count == 0)
            {
                return Item;
            }

            var names = Items
                .Select(item => item.Name.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (names.Count == 0)
            {
                return ItemCount == 1 ? "Unnamed item" : $"{ItemCount} items";
            }

            return ItemCount == 1
                ? names[0]
                : $"{names[0]} + {ItemCount - 1} more";
        }
    }

    [JsonIgnore]
    public string ItemsToolTip
    {
        get
        {
            if (Items.Count == 0)
            {
                return string.IsNullOrWhiteSpace(Item)
                    ? ItemCountSummary
                    : $"{Item.Trim()} (qty {Quantity})";
            }

            var lines = Items
                .Select(item =>
                {
                    var name = item.Name.Trim();
                    return string.IsNullOrWhiteSpace(name)
                        ? string.Empty
                        : $"{name} (qty {item.Quantity})";
                })
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return lines.Count == 0 ? ItemCountSummary : string.Join(Environment.NewLine, lines);
        }
    }

    [JsonIgnore]
    public decimal Subtotal => Items.Count > 0 ? Items.Sum(item => item.Subtotal) : Quantity * UnitPrice;

    [JsonIgnore]
    public decimal TotalCost => Subtotal + ShippingCost + Tax + OtherCost;

    [JsonIgnore]
    public string MerchantIcon => Merchant switch
    {
        MerchantKind.Amazon => "A",
        MerchantKind.Walmart => "W",
        MerchantKind.Target => "T",
        MerchantKind.BestBuy => "BB",
        MerchantKind.eBay => "E",
        MerchantKind.Other => "O",
        _ => "?"
    };

    [JsonIgnore]
    public bool IsCompleted => Status == OrderStatus.Delivered;

    [JsonIgnore]
    public string OrderMonth => OrderDate.ToString("yyyy MMMM");

    [JsonIgnore]
    public string OrderYear => OrderDate.Year.ToString();

    [JsonIgnore]
    public DateTime ExpectedSortDate => ExpectedDate ?? DateTime.MaxValue;

    private void OnCostChanged()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(TotalCost));
    }

    [JsonIgnore]
    public bool HasLegacyItemFields => Items.Count == 0 && !string.IsNullOrWhiteSpace(Item);

    public void NormalizeItemCollection()
    {
        Items = new ObservableCollection<OrderItem>(Items.OfType<OrderItem>());

        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }

            item.RefreshInputs();
        }

        OnItemsChanged();
    }

    public bool MigrateLegacyItemFields()
    {
        if (!HasLegacyItemFields)
        {
            return false;
        }

        Items.Add(new OrderItem
        {
            Name = Item,
            Quantity = Quantity,
            UnitPrice = UnitPrice
        });
        OnItemsChanged();
        return true;
    }

    private void SubscribeItems(ObservableCollection<OrderItem> items)
    {
        items.CollectionChanged += ItemsCollectionChanged;
        foreach (var item in items)
        {
            item.PropertyChanged += OrderItemPropertyChanged;
        }
    }

    private void UnsubscribeItems(ObservableCollection<OrderItem> items)
    {
        items.CollectionChanged -= ItemsCollectionChanged;
        foreach (var item in items)
        {
            item.PropertyChanged -= OrderItemPropertyChanged;
        }
    }

    private void SubscribeTrackingNumbers(ObservableCollection<TrackingEntry> trackingNumbers)
    {
        trackingNumbers.CollectionChanged += TrackingNumbersCollectionChanged;
        foreach (var tracking in trackingNumbers)
        {
            tracking.PropertyChanged += TrackingEntryPropertyChanged;
        }
    }

    private void UnsubscribeTrackingNumbers(ObservableCollection<TrackingEntry> trackingNumbers)
    {
        trackingNumbers.CollectionChanged -= TrackingNumbersCollectionChanged;
        foreach (var tracking in trackingNumbers)
        {
            tracking.PropertyChanged -= TrackingEntryPropertyChanged;
        }
    }

    private void ItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OrderItem item in e.OldItems)
            {
                item.PropertyChanged -= OrderItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OrderItem item in e.NewItems)
            {
                item.PropertyChanged += OrderItemPropertyChanged;
            }
        }

        OnItemsChanged();
    }

    private void TrackingNumbersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TrackingEntry tracking in e.OldItems)
            {
                tracking.PropertyChanged -= TrackingEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (TrackingEntry tracking in e.NewItems)
            {
                tracking.PropertyChanged += TrackingEntryPropertyChanged;
            }
        }

        OnTrackingNumbersChanged();
    }

    private void OrderItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrderItem.Name) or nameof(OrderItem.Quantity) or nameof(OrderItem.UnitPrice) or nameof(OrderItem.Subtotal))
        {
            OnItemsChanged();
        }
    }

    private void TrackingEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrackingEntry.Number)
            or nameof(TrackingEntry.Carrier)
            or nameof(TrackingEntry.Status)
            or nameof(TrackingEntry.Link))
        {
            OnTrackingNumbersChanged();
        }
    }

    private void OnItemsChanged()
    {
        if (!HasItems && IsItemsExpanded)
        {
            IsItemsExpanded = false;
        }

        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasMultipleItems));
        OnPropertyChanged(nameof(HasLegacyItemFields));
        OnPropertyChanged(nameof(ItemCountSummary));
        OnPropertyChanged(nameof(TotalQuantity));
        OnPropertyChanged(nameof(PrimaryItem));
        OnPropertyChanged(nameof(ItemsSummary));
        OnPropertyChanged(nameof(ItemsToolTip));
        OnCostChanged();
    }

    private void OnTrackingNumbersChanged()
    {
        if (!HasMultipleTrackingNumbers && IsTrackingExpanded)
        {
            IsTrackingExpanded = false;
        }

        OnPropertyChanged(nameof(TrackingCount));
        OnPropertyChanged(nameof(HasTrackingNumbers));
        OnPropertyChanged(nameof(HasMultipleTrackingNumbers));
        OnPropertyChanged(nameof(TrackingCountSummary));
        OnPropertyChanged(nameof(PrimaryTracking));
    }
}
