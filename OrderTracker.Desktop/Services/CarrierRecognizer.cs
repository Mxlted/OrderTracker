using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public static partial class CarrierRecognizer
{
    private const string TargetOrderHistoryUrl = "https://www.target.com/orders";
    private const string TargetOrdersBaseUrl = TargetOrderHistoryUrl + "/";

    private static readonly Regex AmazonOrderIdPattern = new(@"^\d{3}-\d{7}-\d{7}$", RegexOptions.Compiled);
    private static readonly Regex UpsPattern = new(@"^1Z[0-9A-Z]{16}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AmazonTrackingPattern = new(@"^TBA[A-Z0-9]{9,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UspsNumericPattern = new(@"^(92|93|94|95|96)\d{18,20}$", RegexOptions.Compiled);
    private static readonly Regex UspsInternationalPattern = new(@"^[A-Z]{2}\d{9}US$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FedExNumericPattern = new(@"^(\d{12}|\d{15}|\d{20}|\d{22})$", RegexOptions.Compiled);

    public static string NormalizeTrackingNumber(string value)
    {
        return Regex.Replace((value ?? string.Empty).Trim(), @"[\s-]+", string.Empty).ToUpperInvariant();
    }

    public static bool IsAmazonOrderId(string value)
    {
        return AmazonOrderIdPattern.IsMatch((value ?? string.Empty).Trim());
    }

    public static CarrierKind RecognizeCarrier(string value)
    {
        var normalized = NormalizeTrackingNumber(value);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return CarrierKind.Unknown;
        }

        if (UpsPattern.IsMatch(normalized))
        {
            return CarrierKind.UPS;
        }

        if (AmazonTrackingPattern.IsMatch(normalized))
        {
            return CarrierKind.Amazon;
        }

        if (UspsNumericPattern.IsMatch(normalized) || UspsInternationalPattern.IsMatch(normalized))
        {
            return CarrierKind.USPS;
        }

        if (FedExNumericPattern.IsMatch(normalized))
        {
            return CarrierKind.FedEx;
        }

        return CarrierKind.Unknown;
    }

    public static MerchantKind RecognizeMerchant(string merchantText, string orderNumber, IEnumerable<TrackingEntry> trackingNumbers)
    {
        merchantText ??= string.Empty;
        orderNumber ??= string.Empty;
        trackingNumbers ??= Enumerable.Empty<TrackingEntry>();

        if (merchantText.Contains("amazon", StringComparison.OrdinalIgnoreCase) || IsAmazonOrderId(orderNumber))
        {
            return MerchantKind.Amazon;
        }

        if (merchantText.Contains("walmart", StringComparison.OrdinalIgnoreCase))
        {
            return MerchantKind.Walmart;
        }

        if (merchantText.Contains("target", StringComparison.OrdinalIgnoreCase))
        {
            return MerchantKind.Target;
        }

        if (merchantText.Contains("best buy", StringComparison.OrdinalIgnoreCase) || merchantText.Contains("bestbuy", StringComparison.OrdinalIgnoreCase))
        {
            return MerchantKind.BestBuy;
        }

        if (merchantText.Contains("ebay", StringComparison.OrdinalIgnoreCase))
        {
            return MerchantKind.eBay;
        }

        if (trackingNumbers.Any(tracking => tracking.Carrier == CarrierKind.Amazon))
        {
            return MerchantKind.Amazon;
        }

        if (string.IsNullOrWhiteSpace(merchantText) ||
            merchantText.Equals(nameof(MerchantKind.Unknown), StringComparison.OrdinalIgnoreCase))
        {
            return MerchantKind.Unknown;
        }

        return MerchantKind.Other;
    }

    public static string BuildAmazonOrderUrl(string orderNumber)
    {
        return $"https://www.amazon.com/your-orders/order-details?orderID={Uri.EscapeDataString(orderNumber.Trim())}";
    }

    public static string BuildAmazonOrderHistoryUrl()
    {
        return "https://www.amazon.com/gp/css/order-history";
    }

    public static string BuildTargetOrderHistoryUrl()
    {
        return TargetOrderHistoryUrl;
    }

    private static string BuildTargetOrderUrl(string orderNumber)
    {
        var trimmed = orderNumber.Trim();
        return string.Concat(TargetOrdersBaseUrl, Uri.EscapeDataString(trimmed));
    }

    public static string BuildOrderUrl(Order order)
    {
        if (order.Merchant == MerchantKind.Amazon && IsAmazonOrderId(order.OrderNumber))
        {
            return BuildAmazonOrderUrl(order.OrderNumber);
        }

        if (!string.IsNullOrWhiteSpace(order.OrderLink))
        {
            return order.OrderLink.Trim();
        }

        if (order.Merchant == MerchantKind.Target && !string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            return BuildTargetOrderUrl(order.OrderNumber);
        }

        return order.OrderLink.Trim();
    }

    public static string BuildTrackingUrl(Order order, TrackingEntry tracking)
    {
        if (order.Merchant == MerchantKind.Amazon && IsAmazonOrderId(order.OrderNumber))
        {
            return BuildAmazonOrderUrl(order.OrderNumber);
        }

        if (!string.IsNullOrWhiteSpace(tracking.Link))
        {
            return tracking.Link.Trim();
        }

        var normalized = NormalizeTrackingNumber(tracking.Number);
        return tracking.Carrier switch
        {
            CarrierKind.UPS => $"https://www.ups.com/track?tracknum={Uri.EscapeDataString(normalized)}",
            CarrierKind.USPS => $"https://tools.usps.com/go/TrackConfirmAction?tLabels={Uri.EscapeDataString(normalized)}",
            CarrierKind.FedEx => $"https://www.fedex.com/fedextrack/?trknbr={Uri.EscapeDataString(normalized)}",
            CarrierKind.Amazon when IsAmazonOrderId(order.OrderNumber) => BuildAmazonOrderUrl(order.OrderNumber),
            _ => order.OrderLink.Trim()
        };
    }

    public static void ApplyRecognition(Order order)
    {
        foreach (var tracking in order.TrackingNumbers)
        {
            tracking.Number = NormalizeTrackingNumber(tracking.Number);
            tracking.Carrier = RecognizeCarrier(tracking.Number);
        }

        if (order.Merchant == MerchantKind.Unknown || order.Merchant == MerchantKind.Other)
        {
            order.Merchant = RecognizeMerchant(order.Merchant.ToString(), order.OrderNumber, order.TrackingNumbers);
        }

        if (order.Merchant == MerchantKind.Amazon && IsAmazonOrderId(order.OrderNumber))
        {
            order.OrderLink = BuildAmazonOrderUrl(order.OrderNumber);
        }
        else if (order.Merchant == MerchantKind.Target &&
                 string.IsNullOrWhiteSpace(order.OrderLink) &&
                 !string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            order.OrderLink = BuildTargetOrderUrl(order.OrderNumber);
        }

        foreach (var tracking in order.TrackingNumbers)
        {
            tracking.Link = BuildTrackingUrl(order, tracking);
        }
    }
}
