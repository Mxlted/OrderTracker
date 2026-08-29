using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class OrderJsonConverter : JsonConverter<Order>
{
    public override Order Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected order object.");
        }

        var order = new Order();
        var hasCreatedAt = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return FinalizeRead(order, hasCreatedAt);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected order property.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (Matches(propertyName, nameof(Order.Id)))
            {
                order.Id = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.CreatedAt)))
            {
                order.CreatedAt = ReadDateTime(ref reader, DateTime.MinValue);
                hasCreatedAt = true;
            }
            else if (Matches(propertyName, nameof(Order.IsArchived)))
            {
                order.IsArchived = ReadBoolean(ref reader, order.IsArchived);
            }
            else if (Matches(propertyName, nameof(Order.AccountEmail)))
            {
                order.AccountEmail = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.Merchant)))
            {
                order.Merchant = ReadMerchantKind(ref reader, order.Merchant);
            }
            else if (Matches(propertyName, nameof(Order.OrderNumber)))
            {
                order.OrderNumber = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.OrderLink)))
            {
                order.OrderLink = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.Item)))
            {
                order.Item = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.Quantity)))
            {
                order.Quantity = ReadInt(ref reader, order.Quantity);
            }
            else if (Matches(propertyName, nameof(Order.UnitPrice)))
            {
                order.UnitPrice = ReadDecimal(ref reader, order.UnitPrice);
            }
            else if (Matches(propertyName, nameof(Order.Items)))
            {
                order.Items = ReadCollection<OrderItem>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.ShippingCost)))
            {
                order.ShippingCost = ReadDecimal(ref reader, order.ShippingCost);
            }
            else if (Matches(propertyName, nameof(Order.Tax)))
            {
                order.Tax = ReadDecimal(ref reader, order.Tax);
            }
            else if (Matches(propertyName, nameof(Order.OtherCost)))
            {
                order.OtherCost = ReadDecimal(ref reader, order.OtherCost);
            }
            else if (Matches(propertyName, nameof(Order.ProjectedRoiPercentOverride)))
            {
                order.ProjectedRoiPercentOverride = ReadNullableDecimal(ref reader, order.ProjectedRoiPercentOverride);
            }
            else if (Matches(propertyName, nameof(Order.ProjectedProfitOverride)))
            {
                order.ProjectedProfitOverride = ReadNullableDecimal(ref reader, order.ProjectedProfitOverride);
            }
            else if (Matches(propertyName, nameof(Order.OrderDate)))
            {
                order.OrderDate = ReadDateTime(ref reader, order.OrderDate);
            }
            else if (Matches(propertyName, nameof(Order.ExpectedDate)))
            {
                order.ExpectedDate = ReadNullableDateTime(ref reader, order.ExpectedDate);
            }
            else if (Matches(propertyName, nameof(Order.DeliveredDate)))
            {
                order.DeliveredDate = ReadNullableDateTime(ref reader, order.DeliveredDate);
            }
            else if (Matches(propertyName, nameof(Order.Status)))
            {
                order.Status = ReadEnum(ref reader, order.Status);
            }
            else if (Matches(propertyName, nameof(Order.StatusBeforeDelivered)))
            {
                order.StatusBeforeDelivered = ReadNullableEnum(ref reader, order.StatusBeforeDelivered);
            }
            else if (Matches(propertyName, nameof(Order.TrackingStatus)))
            {
                order.TrackingStatus = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.TrackingNumbers)))
            {
                order.TrackingNumbers = ReadCollection<TrackingEntry>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.Notes)))
            {
                order.Notes = ReadString(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Expected end of order object.");
    }

    public override void Write(Utf8JsonWriter writer, Order order, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(Order.Id), order.Id);
        WriteProperty(writer, nameof(Order.CreatedAt), order.CreatedAt, options);
        writer.WriteBoolean(nameof(Order.IsArchived), order.IsArchived);
        writer.WriteString(nameof(Order.AccountEmail), order.AccountEmail);
        WriteProperty(writer, nameof(Order.Merchant), order.Merchant, options);
        writer.WriteString(nameof(Order.OrderNumber), order.OrderNumber);
        writer.WriteString(nameof(Order.OrderLink), order.OrderLink);

        if (order.Items.Count == 0)
        {
            writer.WriteString(nameof(Order.Item), order.Item);
            writer.WriteNumber(nameof(Order.Quantity), order.Quantity);
            writer.WriteNumber(nameof(Order.UnitPrice), order.UnitPrice);
        }
        else
        {
            WriteProperty(writer, nameof(Order.Items), order.Items, options);
        }

        writer.WriteNumber(nameof(Order.ShippingCost), order.ShippingCost);
        writer.WriteNumber(nameof(Order.Tax), order.Tax);
        writer.WriteNumber(nameof(Order.OtherCost), order.OtherCost);
        if (order.ProjectedRoiPercentOverride.HasValue)
        {
            writer.WriteNumber(nameof(Order.ProjectedRoiPercentOverride), order.ProjectedRoiPercentOverride.Value);
        }

        if (order.ProjectedProfitOverride.HasValue)
        {
            writer.WriteNumber(nameof(Order.ProjectedProfitOverride), order.ProjectedProfitOverride.Value);
        }

        WriteProperty(writer, nameof(Order.OrderDate), order.OrderDate, options);
        WriteProperty(writer, nameof(Order.ExpectedDate), order.ExpectedDate, options);
        WriteProperty(writer, nameof(Order.DeliveredDate), order.DeliveredDate, options);
        WriteProperty(writer, nameof(Order.Status), order.Status, options);
        if (order.StatusBeforeDelivered.HasValue)
        {
            WriteProperty(writer, "statusBeforeDelivered", order.StatusBeforeDelivered.Value, options);
        }

        writer.WriteString(nameof(Order.TrackingStatus), order.TrackingStatus);
        WriteProperty(writer, nameof(Order.TrackingNumbers), order.TrackingNumbers, options);
        writer.WriteString(nameof(Order.Notes), order.Notes);
        writer.WriteEndObject();
    }

    private static Order FinalizeRead(Order order, bool hasCreatedAt)
    {
        if (!hasCreatedAt)
        {
            order.CreatedAt = DateTime.MinValue;
        }

        return order;
    }

    private static string ReadString(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.TryGetDecimal(out var value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        return reader.TokenType switch
        {
            JsonTokenType.Null => string.Empty,
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.True => bool.TrueString,
            JsonTokenType.False => bool.FalseString,
            _ => SkipAndDefault(ref reader, string.Empty)
        };
    }

    private static bool ReadBoolean(ref Utf8JsonReader reader, bool fallback)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
            JsonTokenType.Number when reader.TryGetInt32(out var value) => value != 0,
            JsonTokenType.Null => fallback,
            _ => SkipAndDefault(ref reader, fallback)
        };
    }

    private static int ReadInt(ref Utf8JsonReader reader, int fallback)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
        {
            return numericValue;
        }

        if (reader.TokenType == JsonTokenType.String &&
            int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        if (reader.TokenType == JsonTokenType.String &&
            int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var currentValue))
        {
            return currentValue;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        return fallback;
    }

    private static decimal ReadDecimal(ref Utf8JsonReader reader, decimal fallback)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numericValue))
        {
            return numericValue;
        }

        if (reader.TokenType == JsonTokenType.String &&
            decimal.TryParse(reader.GetString(), NumberStyles.Currency, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        if (reader.TokenType == JsonTokenType.String &&
            decimal.TryParse(reader.GetString(), NumberStyles.Currency, CultureInfo.CurrentCulture, out var currentValue))
        {
            return currentValue;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        return fallback;
    }

    private static decimal? ReadNullableDecimal(ref Utf8JsonReader reader, decimal? fallback)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var numericValue))
        {
            return numericValue;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (decimal.TryParse(text, NumberStyles.Currency, CultureInfo.InvariantCulture, out var invariantValue))
            {
                return invariantValue;
            }

            if (decimal.TryParse(text, NumberStyles.Currency, CultureInfo.CurrentCulture, out var currentValue))
            {
                return currentValue;
            }
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        return fallback;
    }

    private static DateTime ReadDateTime(ref Utf8JsonReader reader, DateTime fallback)
    {
        return ReadNullableDateTime(ref reader, fallback) ?? fallback;
    }

    private static DateTime? ReadNullableDateTime(ref Utf8JsonReader reader, DateTime? fallback)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var invariantValue))
            {
                return invariantValue;
            }

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var currentValue))
            {
                return currentValue;
            }
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        return fallback;
    }

    private static TEnum ReadEnum<TEnum>(ref Utf8JsonReader reader, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (reader.TokenType == JsonTokenType.String &&
            EnumTextParser.TryReadName(reader.GetString(), out TEnum namedValue))
        {
            return namedValue;
        }

        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var numericValue) &&
            Enum.IsDefined(typeof(TEnum), numericValue))
        {
            return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        JsonReadDiagnostics.RecordSubstitutedValue();
        return fallback;
    }

    private static TEnum? ReadNullableEnum<TEnum>(ref Utf8JsonReader reader, TEnum? fallback)
        where TEnum : struct, Enum
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String &&
            EnumTextParser.TryReadName(reader.GetString(), out TEnum namedValue))
        {
            return namedValue;
        }

        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var numericValue) &&
            Enum.IsDefined(typeof(TEnum), numericValue))
        {
            return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            reader.Skip();
        }

        JsonReadDiagnostics.RecordSubstitutedValue();
        return fallback;
    }

    private static MerchantKind ReadMerchantKind(ref Utf8JsonReader reader, MerchantKind fallback)
    {
        if (MerchantKindJsonConverter.TryReadLegacyValue(ref reader, out var merchant))
        {
            return merchant;
        }

        return ReadEnum(ref reader, fallback);
    }

    private static ObservableCollection<T> ReadCollection<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var collection = new ObservableCollection<T>();
        if (reader.TokenType == JsonTokenType.Null)
        {
            return collection;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return collection;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            try
            {
                var value = element.Deserialize<T>(options);
                if (value is not null)
                {
                    collection.Add(value);
                }
                else
                {
                    JsonReadDiagnostics.RecordSkippedElement();
                }
            }
            catch (JsonException)
            {
                JsonReadDiagnostics.RecordSkippedElement();
            }
            catch (NotSupportedException)
            {
                JsonReadDiagnostics.RecordSkippedElement();
            }
        }

        return collection;
    }

    private static void WriteProperty<T>(Utf8JsonWriter writer, string propertyName, T value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(propertyName);
        JsonSerializer.Serialize(writer, value, options);
    }

    private static bool Matches(string? actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static T SkipAndDefault<T>(ref Utf8JsonReader reader, T fallback)
    {
        reader.Skip();
        return fallback;
    }
}
