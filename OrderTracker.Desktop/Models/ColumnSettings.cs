using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class ColumnSettings : ObservableObject
{
    private bool _showMerchant = true;
    private bool _showAccount = true;
    private bool _showOrderNumber = true;
    private bool _showItem = true;
    private bool _showQuantity = true;
    private bool _showStatus = true;
    private bool _showTracking = true;
    private bool _showDates = true;
    private bool _showTotal = true;
    private bool _showActions = true;

    public bool ShowMerchant
    {
        get => _showMerchant;
        set => SetProperty(ref _showMerchant, value);
    }

    public bool ShowAccount
    {
        get => _showAccount;
        set => SetProperty(ref _showAccount, value);
    }

    public bool ShowOrderNumber
    {
        get => _showOrderNumber;
        set => SetProperty(ref _showOrderNumber, value);
    }

    public bool ShowItem
    {
        get => _showItem;
        set => SetProperty(ref _showItem, value);
    }

    public bool ShowQuantity
    {
        get => _showQuantity;
        set => SetProperty(ref _showQuantity, value);
    }

    public bool ShowStatus
    {
        get => _showStatus;
        set => SetProperty(ref _showStatus, value);
    }

    public bool ShowTracking
    {
        get => _showTracking;
        set => SetProperty(ref _showTracking, value);
    }

    public bool ShowDates
    {
        get => _showDates;
        set => SetProperty(ref _showDates, value);
    }

    public bool ShowTotal
    {
        get => _showTotal;
        set => SetProperty(ref _showTotal, value);
    }

    public bool ShowActions
    {
        get => _showActions;
        set => SetProperty(ref _showActions, value);
    }
}
