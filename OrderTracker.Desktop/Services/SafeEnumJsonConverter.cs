using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderTracker.Desktop.Services;

public sealed class SafeEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly TEnum _fallback;

    public SafeEnumJsonConverter(TEnum fallback)
    {
        _fallback = fallback;
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return TryReadString(reader.GetString(), out var value) ? value : _fallback;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var numericValue))
        {
            return TryReadNumber(numericValue, out var value) ? value : _fallback;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        return _fallback;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    private static bool TryReadString(string? text, out TEnum value)
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

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericValue))
        {
            return TryReadNumber(numericValue, out value);
        }

        var normalizedText = NormalizeEnumName(text);
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(NormalizeEnumName(name), normalizedText, StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<TEnum>(name, out parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadNumber(long numericValue, out TEnum value)
    {
        value = default;
        if (numericValue < int.MinValue || numericValue > int.MaxValue ||
            !Enum.IsDefined(typeof(TEnum), (int)numericValue))
        {
            return false;
        }

        value = (TEnum)Enum.ToObject(typeof(TEnum), (int)numericValue);
        return true;
    }

    private static string NormalizeEnumName(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
