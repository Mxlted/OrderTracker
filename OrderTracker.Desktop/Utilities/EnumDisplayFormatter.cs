using System.Text.RegularExpressions;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Utilities;

public static partial class EnumDisplayFormatter
{
    public static string Format(Enum value)
    {
        return value switch
        {
            MerchantKind.eBay => "eBay",
            CarrierKind.OnTrac => "OnTrac",
            _ => SplitPascalCase().Replace(value.ToString(), "$1 $2")
        };
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SplitPascalCase();
}
