namespace OrderTracker.Desktop.Models;

public static class PresetWorkflowRules
{
    public static bool SupportsAccountOrderHistory(MerchantKind merchant)
    {
        return merchant is MerchantKind.Amazon or MerchantKind.Target;
    }

    public static bool SupportsIsolatedAccountSession(MerchantKind merchant)
    {
        return merchant is MerchantKind.Amazon or MerchantKind.Target;
    }

    public static bool IsMerchantQuickFillMatch(MerchantKind selectedMerchant, MerchantKind presetMerchant)
    {
        return selectedMerchant == MerchantKind.Unknown ||
               presetMerchant == MerchantKind.Unknown ||
               presetMerchant == selectedMerchant;
    }

    public static decimal ApplyMoneyDefault(decimal currentValue, decimal presetDefault)
    {
        return currentValue == 0m ? presetDefault : currentValue;
    }
}
