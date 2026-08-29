using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class MerchantKindJsonConverter : JsonConverter<MerchantKind>
{
    public override MerchantKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (TryReadLegacyValue(ref reader, out var legacyMerchant))
        {
            return legacyMerchant;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            return Enum.TryParse<MerchantKind>(text, ignoreCase: true, out var merchant) &&
                Enum.IsDefined(typeof(MerchantKind), merchant)
                    ? merchant
                    : MerchantKind.Unknown;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
        {
            return Enum.IsDefined(typeof(MerchantKind), numericValue)
                ? (MerchantKind)Enum.ToObject(typeof(MerchantKind), numericValue)
                : MerchantKind.Unknown;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        return MerchantKind.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, MerchantKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    internal static bool TryReadLegacyValue(ref Utf8JsonReader reader, out MerchantKind merchant)
    {
        if ((reader.TokenType == JsonTokenType.String &&
             string.Equals(reader.GetString(), "Etsy", StringComparison.OrdinalIgnoreCase)) ||
            (reader.TokenType == JsonTokenType.Number &&
             reader.TryGetInt32(out var numericValue) &&
             numericValue == 6))
        {
            merchant = MerchantKind.Other;
            return true;
        }

        merchant = default;
        return false;
    }
}
