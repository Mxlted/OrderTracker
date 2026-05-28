using System;
using System.Collections.ObjectModel;
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

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return order;
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
            else if (Matches(propertyName, nameof(Order.IsArchived)))
            {
                order.IsArchived = JsonSerializer.Deserialize<bool>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.AccountEmail)))
            {
                order.AccountEmail = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.Merchant)))
            {
                order.Merchant = JsonSerializer.Deserialize<MerchantKind>(ref reader, options);
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
                order.Quantity = JsonSerializer.Deserialize<int>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.UnitPrice)))
            {
                order.UnitPrice = JsonSerializer.Deserialize<decimal>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.Items)))
            {
                order.Items = JsonSerializer.Deserialize<ObservableCollection<OrderItem>>(ref reader, options) ?? new();
            }
            else if (Matches(propertyName, nameof(Order.ShippingCost)))
            {
                order.ShippingCost = JsonSerializer.Deserialize<decimal>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.Tax)))
            {
                order.Tax = JsonSerializer.Deserialize<decimal>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.OtherCost)))
            {
                order.OtherCost = JsonSerializer.Deserialize<decimal>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.OrderDate)))
            {
                order.OrderDate = JsonSerializer.Deserialize<DateTime>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.ExpectedDate)))
            {
                order.ExpectedDate = JsonSerializer.Deserialize<DateTime?>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.DeliveredDate)))
            {
                order.DeliveredDate = JsonSerializer.Deserialize<DateTime?>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.Status)))
            {
                order.Status = JsonSerializer.Deserialize<OrderStatus>(ref reader, options);
            }
            else if (Matches(propertyName, nameof(Order.TrackingStatus)))
            {
                order.TrackingStatus = ReadString(ref reader);
            }
            else if (Matches(propertyName, nameof(Order.TrackingNumbers)))
            {
                order.TrackingNumbers = JsonSerializer.Deserialize<ObservableCollection<TrackingEntry>>(ref reader, options) ?? new();
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
        WriteProperty(writer, nameof(Order.OrderDate), order.OrderDate, options);
        WriteProperty(writer, nameof(Order.ExpectedDate), order.ExpectedDate, options);
        WriteProperty(writer, nameof(Order.DeliveredDate), order.DeliveredDate, options);
        WriteProperty(writer, nameof(Order.Status), order.Status, options);
        writer.WriteString(nameof(Order.TrackingStatus), order.TrackingStatus);
        WriteProperty(writer, nameof(Order.TrackingNumbers), order.TrackingNumbers, options);
        writer.WriteString(nameof(Order.Notes), order.Notes);
        writer.WriteEndObject();
    }

    private static string ReadString(ref Utf8JsonReader reader)
    {
        return reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty;
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
}
