using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class MerchantKindJsonConverter : JsonConverter<MerchantKind>
{
    public override MerchantKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.Equals(text, "Etsy", StringComparison.OrdinalIgnoreCase))
            {
                return MerchantKind.Other;
            }

            return Enum.TryParse<MerchantKind>(text, ignoreCase: true, out var merchant) &&
                Enum.IsDefined(typeof(MerchantKind), merchant)
                    ? merchant
                    : MerchantKind.Unknown;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
        {
            if (numericValue == 6)
            {
                return MerchantKind.Other;
            }

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
}
