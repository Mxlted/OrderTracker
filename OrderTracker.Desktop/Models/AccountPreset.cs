using System;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class AccountPreset : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _email = string.Empty;
    private MerchantKind _merchantHint = MerchantKind.Unknown;
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
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (SetProperty(ref _email, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public MerchantKind MerchantHint
    {
        get => _merchantHint;
        set => SetProperty(ref _merchantHint, value);
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

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Email : Name;
}
