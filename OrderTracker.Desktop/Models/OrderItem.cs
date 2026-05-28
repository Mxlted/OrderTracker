using System;
using System.Globalization;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Models;

public sealed class OrderItem : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private int _quantity = 1;
    private decimal _unitPrice;
    private string _quantityInput = string.Empty;
    private string _unitPriceInput = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value ?? string.Empty);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            var normalized = Math.Max(1, value);
            if (SetProperty(ref _quantity, normalized))
            {
                QuantityInput = FormatQuantityInput(normalized);
                OnPropertyChanged(nameof(Subtotal));
            }
        }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (SetProperty(ref _unitPrice, value))
            {
                UnitPriceInput = FormatMoneyInput(value);
                OnPropertyChanged(nameof(Subtotal));
            }
        }
    }

    [JsonIgnore]
    public string QuantityInput
    {
        get => _quantityInput;
        set => SetProperty(ref _quantityInput, value ?? string.Empty);
    }

    [JsonIgnore]
    public string UnitPriceInput
    {
        get => _unitPriceInput;
        set => SetProperty(ref _unitPriceInput, value ?? string.Empty);
    }

    [JsonIgnore]
    public decimal Subtotal => Quantity * UnitPrice;

    public OrderItem Clone()
    {
        return new OrderItem
        {
            Name = Name,
            Quantity = Quantity,
            UnitPrice = UnitPrice
        };
    }

    public void RefreshInputs()
    {
        QuantityInput = FormatQuantityInput(Quantity);
        UnitPriceInput = FormatMoneyInput(UnitPrice);
    }

    private static string FormatQuantityInput(int value)
    {
        return value <= 1 ? string.Empty : value.ToString(CultureInfo.CurrentCulture);
    }

    private static string FormatMoneyInput(decimal value)
    {
        return value == 0m ? string.Empty : value.ToString("0.00", CultureInfo.CurrentCulture);
    }
}
