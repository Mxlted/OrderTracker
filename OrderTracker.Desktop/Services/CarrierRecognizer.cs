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

    public static string NormalizeTrackingNumber(string value)
    {
        return TrackingSeparatorPattern().Replace((value ?? string.Empty).Trim(), string.Empty).ToUpperInvariant();
    }

    public static bool IsAmazonOrderId(string value)
    {
        return AmazonOrderIdPattern().IsMatch((value ?? string.Empty).Trim());
    }

    public static CarrierKind RecognizeCarrier(string value)
    {
        var normalized = NormalizeTrackingNumber(value);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return CarrierKind.Unknown;
        }

        if (UpsPattern().IsMatch(normalized))
        {
            return CarrierKind.UPS;
        }

        if (AmazonTrackingPattern().IsMatch(normalized))
        {
            return CarrierKind.Amazon;
        }

        if (UspsNumericPattern().IsMatch(normalized) ||
            UspsCertifiedPattern().IsMatch(normalized) ||
            UspsInternationalPattern().IsMatch(normalized))
        {
            return CarrierKind.USPS;
        }

        if (FedExNumericPattern().IsMatch(normalized))
        {
            return CarrierKind.FedEx;
        }

        if (DhlNumericPattern().IsMatch(normalized) || DhlAlphaNumericPattern().IsMatch(normalized))
        {
            return CarrierKind.DHL;
        }

        if (OnTracPattern().IsMatch(normalized) || LaserShipPattern().IsMatch(normalized))
        {
            return CarrierKind.OnTrac;
        }

        return CarrierKind.Unknown;
    }

    public static MerchantKind RecognizeMerchant(
        string orderLink,
        string orderNumber,
        IEnumerable<string> itemNames,
        IEnumerable<TrackingEntry> trackingNumbers,
        bool includeFreeTextHeuristics)
    {
        orderLink ??= string.Empty;
        orderNumber ??= string.Empty;
        itemNames ??= Enumerable.Empty<string>();
        trackingNumbers ??= Enumerable.Empty<TrackingEntry>();

        if (MerchantFaviconService.TryRecognizeMerchantFromLink(orderLink, out var linkedMerchant))
        {
            return linkedMerchant;
        }

        if (IsAmazonOrderId(orderNumber))
        {
            return MerchantKind.Amazon;
        }

        if (!includeFreeTextHeuristics)
        {
            return MerchantKind.Unknown;
        }

        var merchantMatch = MerchantTextPattern().Match(string.Join(' ', itemNames.Prepend(orderNumber)));
        if (merchantMatch.Success)
        {
            return merchantMatch.Value.ToLowerInvariant() switch
            {
                "amazon" => MerchantKind.Amazon,
                "walmart" => MerchantKind.Walmart,
                "target" => MerchantKind.Target,
                "best buy" or "bestbuy" => MerchantKind.BestBuy,
                "ebay" => MerchantKind.eBay,
                _ => MerchantKind.Unknown
            };
        }

        if (trackingNumbers.Any(tracking => tracking.Carrier == CarrierKind.Amazon))
        {
            return MerchantKind.Amazon;
        }

        return MerchantKind.Unknown;
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

    public static bool TryBuildOrderHistoryUrl(MerchantKind merchant, out string url)
    {
        url = merchant switch
        {
            MerchantKind.Amazon => BuildAmazonOrderHistoryUrl(),
            MerchantKind.Target => BuildTargetOrderHistoryUrl(),
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(url);
    }

    private static string BuildTargetOrderUrl(string orderNumber)
    {
        var trimmed = orderNumber.Trim();
        return string.Concat(TargetOrdersBaseUrl, Uri.EscapeDataString(trimmed));
    }

    public static string BuildOrderUrl(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.OrderLink))
        {
            return order.OrderLink.Trim();
        }

        if (order.Merchant == MerchantKind.Amazon && IsAmazonOrderId(order.OrderNumber))
        {
            return BuildAmazonOrderUrl(order.OrderNumber);
        }

        if (order.Merchant == MerchantKind.Target && !string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            return BuildTargetOrderUrl(order.OrderNumber);
        }

        return string.Empty;
    }

    public static string BuildTrackingUrl(Order order, TrackingEntry tracking)
    {
        var normalized = NormalizeTrackingNumber(tracking.Number);
        var carrierUrl = BuildRecognizedCarrierUrl(order, normalized, RecognizeCarrier(normalized));
        if (!string.IsNullOrWhiteSpace(carrierUrl))
        {
            return carrierUrl;
        }

        if (!string.IsNullOrWhiteSpace(tracking.Link))
        {
            return tracking.Link.Trim();
        }

        if (order.Merchant == MerchantKind.Amazon && IsAmazonOrderId(order.OrderNumber))
        {
            return BuildAmazonOrderUrl(order.OrderNumber);
        }

        return string.Empty;
    }

    public static bool ApplyRecognition(Order order)
    {
        var changed = false;

        foreach (var tracking in order.TrackingNumbers)
        {
            var normalizedNumber = NormalizeTrackingNumber(tracking.Number);
            if (!string.Equals(tracking.Number, normalizedNumber, StringComparison.Ordinal))
            {
                tracking.Number = normalizedNumber;
                changed = true;
            }

            var carrier = RecognizeCarrier(tracking.Number);
            if (tracking.Carrier != carrier)
            {
                tracking.Carrier = carrier;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(tracking.Link) && IsLegacyDerivedTrackingLink(order, tracking, carrier))
            {
                tracking.Link = string.Empty;
                changed = true;
            }
        }

        if (order.Merchant == MerchantKind.Unknown || order.Merchant == MerchantKind.Other)
        {
            var merchant = RecognizeMerchant(
                order.OrderLink,
                order.OrderNumber,
                order.Items.Select(item => item.Name),
                order.TrackingNumbers,
                includeFreeTextHeuristics: order.Merchant == MerchantKind.Unknown);
            if (merchant is not MerchantKind.Unknown and not MerchantKind.Other && order.Merchant != merchant)
            {
                order.Merchant = merchant;
                changed = true;
            }
        }

        if (order.Merchant == MerchantKind.Amazon && IsAmazonOrderId(order.OrderNumber))
        {
            var orderLink = BuildAmazonOrderUrl(order.OrderNumber);
            var shouldSetOrderLink = string.IsNullOrWhiteSpace(order.OrderLink) ||
                (TryGetCanonicalAmazonOrderId(order.OrderLink, out var linkedOrderNumber) &&
                 !string.Equals(linkedOrderNumber, order.OrderNumber.Trim(), StringComparison.Ordinal));
            if (shouldSetOrderLink && !string.Equals(order.OrderLink, orderLink, StringComparison.Ordinal))
            {
                order.OrderLink = orderLink;
                changed = true;
            }
        }
        else if (order.Merchant == MerchantKind.Target &&
                 string.IsNullOrWhiteSpace(order.OrderLink) &&
                 !string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            var orderLink = BuildTargetOrderUrl(order.OrderNumber);
            if (!string.Equals(order.OrderLink, orderLink, StringComparison.Ordinal))
            {
                order.OrderLink = orderLink;
                changed = true;
            }
        }

        return changed;
    }

    private static string BuildRecognizedCarrierUrl(Order order, string normalized, CarrierKind carrier)
    {
        return carrier switch
        {
            CarrierKind.UPS => $"https://www.ups.com/track?tracknum={Uri.EscapeDataString(normalized)}",
            CarrierKind.USPS => $"https://tools.usps.com/go/TrackConfirmAction?tLabels={Uri.EscapeDataString(normalized)}",
            CarrierKind.FedEx => $"https://www.fedex.com/fedextrack/?trknbr={Uri.EscapeDataString(normalized)}",
            CarrierKind.Amazon when !IsAmazonOrderId(order.OrderNumber) =>
                $"https://track.amazon.com/tracking/{Uri.EscapeDataString(normalized)}",
            CarrierKind.DHL => $"https://www.dhl.com/us-en/home/tracking.html?tracking-id={Uri.EscapeDataString(normalized)}&submit=1",
            CarrierKind.OnTrac => $"https://www.ontrac.com/tracking/?number={Uri.EscapeDataString(normalized)}",
            _ => string.Empty
        };
    }

    private static bool IsLegacyDerivedTrackingLink(Order order, TrackingEntry tracking, CarrierKind carrier)
    {
        var trimmed = tracking.Link.Trim();
        if (!string.IsNullOrWhiteSpace(order.OrderLink) &&
            string.Equals(trimmed, order.OrderLink.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var carrierUrl = BuildRecognizedCarrierUrl(order, NormalizeTrackingNumber(tracking.Number), carrier);
        if (!string.IsNullOrWhiteSpace(carrierUrl) &&
            string.Equals(trimmed, carrierUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsAmazonOrderId(order.OrderNumber) &&
            string.Equals(trimmed, BuildAmazonOrderUrl(order.OrderNumber), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCanonicalAmazonOrderId(string link, out string orderNumber)
    {
        orderNumber = string.Empty;
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("www.amazon.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/your-orders/order-details", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string queryPrefix = "?orderID=";
        if (!uri.Query.StartsWith(queryPrefix, StringComparison.OrdinalIgnoreCase) || uri.Query.Contains('&'))
        {
            return false;
        }

        orderNumber = Uri.UnescapeDataString(uri.Query[queryPrefix.Length..]);
        return IsAmazonOrderId(orderNumber);
    }

    [GeneratedRegex(@"^[0-9]{3}-[0-9]{7}-[0-9]{7}$")]
    private static partial Regex AmazonOrderIdPattern();

    [GeneratedRegex(@"^1Z[0-9A-Z]{16}$", RegexOptions.IgnoreCase)]
    private static partial Regex UpsPattern();

    [GeneratedRegex(@"^TBA[A-Z0-9]{9,}$", RegexOptions.IgnoreCase)]
    private static partial Regex AmazonTrackingPattern();

    [GeneratedRegex(@"^(92|93|94|95)[0-9]{18}([0-9]{2})?$")]
    private static partial Regex UspsNumericPattern();

    [GeneratedRegex(@"^(70|71|72|73|77|82|23)[0-9]{18}$")]
    private static partial Regex UspsCertifiedPattern();

    [GeneratedRegex(@"^[A-Z]{2}[0-9]{9}US$", RegexOptions.IgnoreCase)]
    private static partial Regex UspsInternationalPattern();

    [GeneratedRegex(@"^([0-9]{12}|[0-9]{15}|[0-9]{20}|[0-9]{22})$")]
    private static partial Regex FedExNumericPattern();

    [GeneratedRegex(@"^[0-9]{10}$")]
    private static partial Regex DhlNumericPattern();

    [GeneratedRegex(@"^(JJD|JVGL|GM)[0-9A-Z]{10,}$", RegexOptions.IgnoreCase)]
    private static partial Regex DhlAlphaNumericPattern();

    [GeneratedRegex(@"^[CD][0-9]{14}$", RegexOptions.IgnoreCase)]
    private static partial Regex OnTracPattern();

    [GeneratedRegex(@"^(1LS|LS|LX)[0-9]{8,}$", RegexOptions.IgnoreCase)]
    private static partial Regex LaserShipPattern();

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex TrackingSeparatorPattern();

    [GeneratedRegex(@"\b(amazon|walmart|target|best ?buy|ebay)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MerchantTextPattern();
}
