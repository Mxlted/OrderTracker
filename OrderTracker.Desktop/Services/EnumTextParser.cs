namespace OrderTracker.Desktop.Services;

internal static class EnumTextParser
{
    public static bool TryReadName<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(TEnum), parsed))
        {
            value = parsed;
            return true;
        }

        var normalizedText = NormalizeName(text);
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(NormalizeName(name), normalizedText, StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<TEnum>(name, out parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeName(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
