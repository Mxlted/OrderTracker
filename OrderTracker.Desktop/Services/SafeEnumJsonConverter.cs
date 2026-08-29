using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderTracker.Desktop.Services;

internal static class JsonReadDiagnostics
{
    [ThreadStatic]
    private static int _skippedElements;

    [ThreadStatic]
    private static int _substitutedValues;

    public static int SkippedElements => _skippedElements;

    public static int SubstitutedValues => _substitutedValues;

    public static void Reset()
    {
        _skippedElements = 0;
        _substitutedValues = 0;
    }

    public static void RecordSkippedElement()
    {
        _skippedElements++;
    }

    public static void RecordSubstitutedValue()
    {
        _substitutedValues++;
    }
}

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
            if (TryReadString(reader.GetString(), out var value))
            {
                return value;
            }

            JsonReadDiagnostics.RecordSubstitutedValue();
            return _fallback;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var numericValue))
        {
            if (TryReadNumber(numericValue, out var value))
            {
                return value;
            }

            JsonReadDiagnostics.RecordSubstitutedValue();
            return _fallback;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        JsonReadDiagnostics.RecordSubstitutedValue();
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

        if (EnumTextParser.TryReadName(text, out value))
        {
            return true;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericValue))
        {
            return TryReadNumber(numericValue, out value);
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
}
