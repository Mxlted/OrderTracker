using System;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class ItemPreset : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _category = string.Empty;
    private MerchantKind _merchantHint = MerchantKind.Unknown;
    private int _defaultQuantity = 1;
    private decimal _defaultUnitPrice;
    private decimal _defaultShipping;
    private decimal _defaultTax;
    private bool _isFavorite;
    private int _usageCount;
    private string _notes = string.Empty;
    private bool _isSelected;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value ?? string.Empty);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value ?? string.Empty);
    }

    public MerchantKind MerchantHint
    {
        get => _merchantHint;
        set => SetProperty(ref _merchantHint, value);
    }

    public int DefaultQuantity
    {
        get => _defaultQuantity;
        set => SetProperty(ref _defaultQuantity, Math.Max(1, value));
    }

    public decimal DefaultUnitPrice
    {
        get => _defaultUnitPrice;
        set => SetProperty(ref _defaultUnitPrice, value);
    }

    public decimal DefaultShipping
    {
        get => _defaultShipping;
        set => SetProperty(ref _defaultShipping, value);
    }

    public decimal DefaultTax
    {
        get => _defaultTax;
        set => SetProperty(ref _defaultTax, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public int UsageCount
    {
        get => _usageCount;
        set => SetProperty(ref _usageCount, Math.Max(0, value));
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? string.Empty);
    }

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
