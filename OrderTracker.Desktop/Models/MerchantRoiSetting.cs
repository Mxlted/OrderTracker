using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class MerchantRoiSetting : ObservableObject
{
    private MerchantKind _merchant = MerchantKind.Unknown;
    private decimal _projectedRoiPercent = AppSettings.DefaultProjectedRoiPercent;

    public MerchantKind Merchant
    {
        get => _merchant;
        set => SetProperty(ref _merchant, value);
    }

    public decimal ProjectedRoiPercent
    {
        get => _projectedRoiPercent;
        set => SetProperty(ref _projectedRoiPercent, Math.Max(0m, value));
    }
}
