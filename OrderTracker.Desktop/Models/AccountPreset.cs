using System;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class AccountPreset : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private DateTime _createdAt = DateTime.MinValue;
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

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
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
                OnPropertyChanged(nameof(EmailDomainGroup));
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
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGroup));
            }
        }
    }

    public int UsageCount
    {
        get => _usageCount;
        set
        {
            if (SetProperty(ref _usageCount, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(UsageGroup));
            }
        }
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

    public string FavoriteGroup => IsFavorite ? "Favorites" : "Not favorites";

    public string UsageGroup => UsageCount switch
    {
        <= 0 => "Unused",
        1 => "Used once",
        <= 4 => "Used 2-4 times",
        _ => "Used 5+ times"
    };

    public string EmailDomainGroup
    {
        get
        {
            var atIndex = Email.LastIndexOf('@');
            if (atIndex < 0 || atIndex == Email.Length - 1)
            {
                return "No domain";
            }

            return Email[(atIndex + 1)..].Trim().ToLowerInvariant();
        }
    }
}
