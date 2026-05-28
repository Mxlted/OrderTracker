using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class DiscordWebhookService
{
    private readonly HttpClient _httpClient = new();

    public async Task<string> SendStatsAsync(IEnumerable<Order> orders, AppSettings settings)
    {
        if (!settings.DiscordEnabled)
        {
            return "Discord webhook is disabled.";
        }

        if (!Uri.TryCreate(settings.DiscordWebhookUrl, UriKind.Absolute, out var webhookUri) ||
            webhookUri.Scheme != Uri.UriSchemeHttps)
        {
            return "Discord webhook URL must be a valid HTTPS URL.";
        }

        var orderList = orders.ToList();
        var openOrders = orderList.Where(IsOpenOrder).ToList();
        var open = openOrders.Count;
        var delivered = orderList.Count(order => order.Status == OrderStatus.Delivered);
        var today = DateTime.Today;
        var overdue = openOrders.Count(order => order.ExpectedDate.HasValue && order.ExpectedDate.Value.Date < today);
        var delayed = openOrders.Count(order => order.Status == OrderStatus.Delayed);
        var openMissingTracking = CountOpenMissingTracking(openOrders);
        var openBalance = openOrders.Sum(order => order.TotalCost);
        var totalSpend = orderList.Sum(order => order.TotalCost);
        var monthOrders = GetCurrentMonthOrders(orderList);
        var yearOrders = GetCurrentYearOrders(orderList);
        var monthSpend = monthOrders.Sum(order => order.TotalCost);
        var yearSpend = yearOrders.Sum(order => order.TotalCost);
        var projectedMonthRoi = CalculateProjectedRoi(monthOrders, settings);
        var projectedYearRoi = CalculateProjectedRoi(yearOrders, settings);
        var color = overdue > 0 || delayed > 0 ? 0xFAA61A : 0x57F287;

        var fields = new List<object>();
        AddField(fields, "Money", BuildMoneySummary(openBalance, totalSpend, monthSpend), inline: true);
        AddField(fields, "Projected ROI", BuildProjectedRoi(monthSpend, yearSpend, projectedMonthRoi, projectedYearRoi), inline: true);
        AddField(fields, "Needs attention", BuildAttentionSummary(openOrders, overdue, delayed, openMissingTracking), inline: true);
        AddField(fields, "Tracking", BuildTrackingSummary(orderList, openMissingTracking), inline: true);
        AddField(fields, "Status", BuildStatusSummary(orderList), inline: true);
        AddField(fields, "Top merchants", BuildTopMerchants(orderList), inline: true);

        var payload = JsonSerializer.Serialize(new
        {
            username = "Order Tracker",
            allowed_mentions = new
            {
                parse = Array.Empty<string>()
            },
            embeds = new[]
            {
                new
                {
                    title = "Order Tracker Overview",
                    description = BuildOverview(open, openBalance, delivered, projectedMonthRoi, overdue + delayed),
                    color,
                    fields = fields.ToArray(),
                    footer = new
                    {
                        text = "Order Tracker desktop overview"
                    },
                    timestamp = DateTimeOffset.Now.ToString("O")
                }
            }
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(webhookUri, content).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? "Discord stats sent."
            : $"Discord returned {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    private static void AddField(List<object> fields, string name, string value, bool inline = false)
    {
        fields.Add(new
        {
            name = Truncate(name, 256),
            value = Truncate(string.IsNullOrWhiteSpace(value) ? "No data yet." : value, 1024),
            inline
        });
    }

    private static string BuildOverview(int open, decimal openBalance, int delivered, decimal projectedMonthRoi, int urgentCount)
    {
        var overview = new StringBuilder()
            .AppendLine($"**{open} open** orders with **{FormatMoney(openBalance)}** outstanding")
            .Append($"**{delivered} delivered** | **{FormatMoney(projectedMonthRoi)}** projected ROI this month");

        if (urgentCount > 0)
        {
            overview.AppendLine()
                .Append($"**{urgentCount}** overdue or delayed");
        }

        return overview.ToString();
    }

    private static string BuildMoneySummary(decimal openBalance, decimal totalSpend, decimal monthSpend)
    {
        return new StringBuilder()
            .AppendLine($"Month **{FormatMoney(monthSpend)}**")
            .AppendLine($"Open balance **{FormatMoney(openBalance)}**")
            .Append($"All time **{FormatMoney(totalSpend)}**")
            .ToString();
    }

    private static string BuildProjectedRoi(
        decimal monthSpend,
        decimal yearSpend,
        decimal projectedMonthRoi,
        decimal projectedYearRoi)
    {
        var monthEffectiveRate = CalculateEffectiveRoiPercent(monthSpend, projectedMonthRoi);
        var yearEffectiveRate = CalculateEffectiveRoiPercent(yearSpend, projectedYearRoi);
        var blendedRate = yearSpend > 0m ? yearEffectiveRate : monthEffectiveRate;

        return new StringBuilder()
            .AppendLine($"Month **{FormatMoney(projectedMonthRoi)}**")
            .AppendLine($"Year **{FormatMoney(projectedYearRoi)}**")
            .Append($"Blended rate **{FormatPercent(blendedRate)}**")
            .ToString();
    }

    private static IReadOnlyList<Order> GetCurrentMonthOrders(IEnumerable<Order> orders)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        return orders
            .Where(order => order.OrderDate >= monthStart && order.OrderDate < monthEnd)
            .ToList();
    }

    private static IReadOnlyList<Order> GetCurrentYearOrders(IEnumerable<Order> orders)
    {
        var today = DateTime.Today;
        var yearStart = new DateTime(today.Year, 1, 1);
        var yearEnd = yearStart.AddYears(1);
        return orders
            .Where(order => order.OrderDate >= yearStart && order.OrderDate < yearEnd)
            .ToList();
    }

    private static decimal CalculateProjectedRoi(IEnumerable<Order> orders, AppSettings settings)
    {
        return orders.Sum(order => CalculateProjectedRoi(order.TotalCost, settings.GetProjectedRoiPercent(order.Merchant)));
    }

    private static decimal CalculateProjectedRoi(decimal spend, decimal percent)
    {
        return spend * Math.Max(0m, percent) / 100m;
    }

    private static decimal CalculateEffectiveRoiPercent(decimal spend, decimal projectedRoi)
    {
        return spend <= 0m ? 0m : projectedRoi / spend * 100m;
    }

    private static string BuildTrackingSummary(IReadOnlyCollection<Order> orders, int openMissingTracking)
    {
        if (orders.Count == 0)
        {
            return "No orders yet.";
        }

        var withTracking = orders.Count(order => order.TrackingNumbers.Any(entry => !string.IsNullOrWhiteSpace(entry.Number)));

        return new StringBuilder()
            .AppendLine($"Tracked orders **{withTracking}/{orders.Count}**")
            .Append(openMissingTracking == 0
                ? "All open orders have tracking."
                : $"Open missing **{openMissingTracking}**")
            .ToString();
    }

    private static string BuildAttentionSummary(IReadOnlyCollection<Order> openOrders, int overdue, int delayed, int openMissingTracking)
    {
        var today = DateTime.Today;
        var openWithExpected = openOrders
            .Where(order => order.ExpectedDate.HasValue)
            .ToList();
        var nextExpected = openWithExpected
            .Where(order => order.ExpectedDate!.Value.Date >= today)
            .OrderBy(order => order.ExpectedDate!.Value)
            .Select(order => order.ExpectedDate!.Value)
            .FirstOrDefault();
        var lines = new List<string>();

        if (overdue > 0)
        {
            lines.Add($"Overdue **{overdue}**");
        }

        if (delayed > 0)
        {
            lines.Add($"Delayed **{delayed}**");
        }

        if (openMissingTracking > 0)
        {
            lines.Add($"Missing tracking **{openMissingTracking}**");
        }

        if (lines.Count == 0)
        {
            lines.Add("No overdue, delayed, or untracked open orders.");
        }

        lines.Add(nextExpected == default ? "Next ETA **None**" : $"Next ETA **{FormatDate(nextExpected)}**");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildStatusSummary(IReadOnlyCollection<Order> orders)
    {
        if (orders.Count == 0)
        {
            return "No orders yet.";
        }

        return string.Join(Environment.NewLine, orders
            .GroupBy(order => order.Status)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => FormatEnum(group.Key))
            .Take(3)
            .Select(group => $"{FormatEnum(group.Key)} **{group.Count()}**"));
    }

    private static string BuildTopMerchants(IReadOnlyCollection<Order> orders)
    {
        if (orders.Count == 0)
        {
            return "No merchant history yet.";
        }

        return string.Join(Environment.NewLine, orders
            .GroupBy(order => order.Merchant)
            .OrderByDescending(group => group.Sum(order => order.TotalCost))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => FormatEnum(group.Key))
            .Take(2)
            .Select(group => $"**{FormatEnum(group.Key)}** {FormatMoney(group.Sum(order => order.TotalCost))} ({group.Count()})"));
    }

    private static int CountOpenMissingTracking(IEnumerable<Order> openOrders)
    {
        return openOrders.Count(order => !order.TrackingNumbers.Any(entry => !string.IsNullOrWhiteSpace(entry.Number)));
    }

    private static bool IsFinalStatus(OrderStatus status)
    {
        return status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned;
    }

    private static bool IsOpenOrder(Order order)
    {
        return !IsFinalStatus(order.Status);
    }

    private static string FormatMoney(decimal amount)
    {
        return amount.ToString("C", CultureInfo.CurrentCulture);
    }

    private static string FormatPercent(decimal percent)
    {
        return string.Concat(Math.Max(0m, percent).ToString("0.##", CultureInfo.CurrentCulture), "%");
    }

    private static string FormatDate(DateTime date)
    {
        return date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    private static string FormatEnum<T>(T value)
        where T : Enum
    {
        return value switch
        {
            MerchantKind.BestBuy => "Best Buy",
            MerchantKind.eBay => "eBay",
            CarrierKind.FedEx => "FedEx",
            OrderStatus.OutForDelivery => "Out for delivery",
            _ => value.ToString()
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return maxLength <= 3 ? value[..maxLength] : string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }
}
