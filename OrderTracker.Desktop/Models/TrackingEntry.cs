using System;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class TrackingEntry : ObservableObject
{
    private string _number = string.Empty;
    private CarrierKind _carrier = CarrierKind.Unknown;
    private string _status = string.Empty;
    private string _link = string.Empty;
    private DateTime _createdAt = DateTime.Now;

    public string Number
    {
        get => _number;
        set => SetProperty(ref _number, value ?? string.Empty);
    }

    public CarrierKind Carrier
    {
        get => _carrier;
        set => SetProperty(ref _carrier, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value ?? string.Empty);
    }

    public string Link
    {
        get => _link;
        set => SetProperty(ref _link, value ?? string.Empty);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }
}
