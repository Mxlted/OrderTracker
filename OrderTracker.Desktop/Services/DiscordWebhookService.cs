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
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    private const int DiscordDescriptionLimit = 4096;
    private const int DiscordFieldNameLimit = 256;
    private const int DiscordFieldValueLimit = 1024;
    private const int TopMerchantLimit = 5;
    private const int AttentionExampleLimit = 3;
    private const int AttentionExampleLineLimit = 96;
    private const int EmbedColorNeutral = 0x5865F2;
    private const int EmbedColorHealthy = 0x57F287;
    private const int EmbedColorWarning = 0xFAA61A;
    private const int EmbedColorCritical = 0xED4245;

    private readonly HttpClient _httpClient = new()
    {
        Timeout = SendTimeout
    };

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
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var openOrders = orderList.Where(order => !order.IsArchived && IsOpenOrder(order)).ToList();
        var open = openOrders.Count;
        var deliveredThisMonth = orderList.Count(order =>
            order.Status == OrderStatus.Delivered &&
            order.DeliveredDate.HasValue &&
            order.DeliveredDate.Value.Date >= monthStart &&
            order.DeliveredDate.Value.Date < monthEnd);
        var overdue = openOrders.Count(order => IsOverdue(order, today));
        var delayed = openOrders.Count(order => order.Status == OrderStatus.Delayed);
        var openMissingTracking = CountOpenMissingTracking(openOrders);
        var attentionOrders = openOrders
            .Where(order => IsAttentionNeeded(order, today))
            .ToList();
        var openBalance = openOrders.Sum(order => order.TotalCost);
        var totalSpend = orderList.Sum(order => order.TotalCost);
        var monthOrders = GetCurrentMonthOrders(orderList);
        var yearOrders = GetCurrentYearOrders(orderList);
        var monthSpend = monthOrders.Sum(order => order.TotalCost);
        var yearSpend = yearOrders.Sum(order => order.TotalCost);
        var projectedMonthRoi = CalculateProjectedRoi(monthOrders, settings);
        var projectedYearRoi = CalculateProjectedRoi(yearOrders, settings);
        var trackedOrders = orderList.Count(HasTracking);
        var nextExpectedDate = GetNextExpectedDate(openOrders, today);
        var color = GetEmbedColor(orderList.Count, overdue, delayed, openMissingTracking);

        var fields = new List<object>();
        AddField(fields, "📦 Orders", BuildOrdersSummary(open, deliveredThisMonth, attentionOrders.Count, orderList.Count), inline: true);
        AddField(fields, "💰 Spend", BuildSpendSummary(monthSpend, totalSpend, openBalance), inline: true);
        AddField(fields, "📈 Projected ROI", BuildProjectedRoi(monthSpend, yearSpend, projectedMonthRoi, projectedYearRoi), inline: true);
        AddField(fields, "🚚 Tracking", BuildTrackingSummary(orderList.Count, trackedOrders, openMissingTracking, nextExpectedDate), inline: true);
        AddField(fields, "⚠️ Needs Attention", BuildAttentionSummary(attentionOrders, overdue, delayed, openMissingTracking, today));
        AddField(fields, "🏪 Top Merchants", BuildTopMerchants(orderList));

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
                    title = "📦 Order Tracker Summary",
                    description = Truncate(
                        BuildOverview(open, openBalance, deliveredThisMonth, projectedMonthRoi, attentionOrders.Count),
                        DiscordDescriptionLimit),
                    color,
                    fields = fields.ToArray(),
                    footer = new
                    {
                        text = "Order Tracker desktop overview"
                    },
                    timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)
                }
            }
        });

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(webhookUri, content).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? "Discord stats sent."
                : $"Discord returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        }
        catch (TaskCanceledException)
        {
            return "Discord send timed out.";
        }
        catch (HttpRequestException ex)
        {
            return $"Discord send failed: {ex.Message}";
        }
    }

    private static void AddField(List<object> fields, string name, string value, bool inline = false)
    {
        fields.Add(new
        {
            name = Truncate(name, DiscordFieldNameLimit),
            value = Truncate(string.IsNullOrWhiteSpace(value) ? "No data yet." : value, DiscordFieldValueLimit),
            inline
        });
    }

    private static string BuildOverview(
        int open,
        decimal openBalance,
        int deliveredThisMonth,
        decimal projectedMonthRoi,
        int needsAttention)
    {
        var lines = new List<string>
        {
            $"**{FormatNumber(open)}** open orders · **{FormatMoney(openBalance)}** open balance",
            $"**{FormatNumber(deliveredThisMonth)}** delivered this month · **{FormatMoney(projectedMonthRoi)}** projected ROI"
        };

        if (needsAttention > 0)
        {
            lines.Add(needsAttention == 1
                ? "**1** order needs attention"
                : $"**{FormatNumber(needsAttention)}** orders need attention");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOrdersSummary(int open, int deliveredThisMonth, int needsAttention, int totalOrders)
    {
        var status = totalOrders == 0
            ? "No orders yet"
            : needsAttention == 0
                ? "All caught up"
                : needsAttention == 1
                    ? "1 order needs attention"
                    : $"{FormatNumber(needsAttention)} orders need attention";

        return new StringBuilder()
            .AppendLine($"Open: **{FormatNumber(open)}**")
            .AppendLine($"Delivered this month: **{FormatNumber(deliveredThisMonth)}**")
            .Append($"Status: **{status}**")
            .ToString();
    }

    private static string BuildSpendSummary(decimal monthSpend, decimal totalSpend, decimal openBalance)
    {
        return new StringBuilder()
            .AppendLine($"This month: **{FormatMoney(monthSpend)}**")
            .AppendLine($"All time: **{FormatMoney(totalSpend)}**")
            .Append($"Open balance: **{FormatMoney(openBalance)}**")
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
            .AppendLine($"Month: **{FormatMoney(projectedMonthRoi)}**")
            .AppendLine($"Year: **{FormatMoney(projectedYearRoi)}**")
            .Append($"Blended rate: **{FormatPercent(blendedRate)}**")
            .ToString();
    }

    private static string BuildTrackingSummary(
        int totalOrders,
        int trackedOrders,
        int openMissingTracking,
        DateTime? nextExpectedDate)
    {
        var nextEta = nextExpectedDate.HasValue ? FormatDate(nextExpectedDate.Value) : "—";

        return new StringBuilder()
            .AppendLine($"Tracked: **{FormatNumber(trackedOrders)}/{FormatNumber(totalOrders)}**")
            .AppendLine($"Untracked open orders: **{FormatNumber(openMissingTracking)}**")
            .Append($"Next ETA: **{nextEta}**")
            .ToString();
    }

    private static string BuildAttentionSummary(
        IReadOnlyCollection<Order> attentionOrders,
        int overdue,
        int delayed,
        int openMissingTracking,
        DateTime today)
    {
        if (attentionOrders.Count == 0)
        {
            return "No overdue, delayed, or untracked open orders.";
        }

        var lines = new List<string>();
        if (overdue > 0)
        {
            lines.Add($"Overdue: **{FormatNumber(overdue)}**");
        }

        if (delayed > 0)
        {
            lines.Add($"Delayed: **{FormatNumber(delayed)}**");
        }

        if (openMissingTracking > 0)
        {
            lines.Add($"Untracked open orders: **{FormatNumber(openMissingTracking)}**");
        }

        var examples = attentionOrders
            .OrderByDescending(order => GetAttentionSeverity(order, today))
            .ThenBy(order => order.ExpectedDate ?? DateTime.MaxValue)
            .ThenByDescending(order => order.OrderDate)
            .Take(AttentionExampleLimit)
            .Select(order => BuildAttentionExample(order, today))
            .Where(example => !string.IsNullOrWhiteSpace(example))
            .ToList();

        if (examples.Count > 0)
        {
            lines.Add("Examples:");
            lines.AddRange(examples);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildAttentionExample(Order order, DateTime today)
    {
        var reasons = new List<string>();
        if (IsOverdue(order, today) && order.ExpectedDate.HasValue)
        {
            reasons.Add($"overdue since {FormatDate(order.ExpectedDate.Value)}");
        }

        if (order.Status == OrderStatus.Delayed)
        {
            reasons.Add("delayed");
        }

        if (!HasTracking(order))
        {
            reasons.Add("missing tracking");
        }

        if (reasons.Count == 0)
        {
            return string.Empty;
        }

        return Truncate($"• {FormatOrderReference(order)} — {string.Join(", ", reasons)}", AttentionExampleLineLimit);
    }

    private static string BuildTopMerchants(IReadOnlyCollection<Order> orders)
    {
        if (orders.Count == 0)
        {
            return "No orders to rank yet.";
        }

        return string.Join(Environment.NewLine, orders
            .GroupBy(order => order.Merchant)
            .OrderByDescending(group => group.Sum(order => order.TotalCost))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => FormatEnum(group.Key))
            .Take(TopMerchantLimit)
            .Select((group, index) =>
                $"{index + 1}. {FormatEnum(group.Key)} — **{FormatMoney(group.Sum(order => order.TotalCost))}** · {FormatOrderCount(group.Count())}"));
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

    private static DateTime? GetNextExpectedDate(IEnumerable<Order> openOrders, DateTime today)
    {
        return openOrders
            .Where(order => order.ExpectedDate.HasValue && order.ExpectedDate.Value.Date >= today)
            .OrderBy(order => order.ExpectedDate!.Value)
            .Select(order => (DateTime?)order.ExpectedDate!.Value)
            .FirstOrDefault();
    }

    private static decimal CalculateProjectedRoi(IEnumerable<Order> orders, AppSettings settings)
    {
        return orders.Sum(order => CalculateProjectedRoi(order.TotalCost, settings.GetProjectedRoiPercent(order)));
    }

    private static decimal CalculateProjectedRoi(decimal spend, decimal percent)
    {
        return spend * Math.Max(0m, percent) / 100m;
    }

    private static decimal CalculateEffectiveRoiPercent(decimal spend, decimal projectedRoi)
    {
        return spend <= 0m ? 0m : projectedRoi / spend * 100m;
    }

    private static int CountOpenMissingTracking(IEnumerable<Order> openOrders)
    {
        return openOrders.Count(order => !HasTracking(order));
    }

    private static int GetAttentionSeverity(Order order, DateTime today)
    {
        if (IsOverdue(order, today) || order.Status == OrderStatus.Delayed)
        {
            return 2;
        }

        return HasTracking(order) ? 0 : 1;
    }

    private static int GetEmbedColor(int orderCount, int overdue, int delayed, int openMissingTracking)
    {
        if (overdue > 0 || delayed > 0)
        {
            return EmbedColorCritical;
        }

        if (openMissingTracking > 0)
        {
            return EmbedColorWarning;
        }

        return orderCount == 0 ? EmbedColorNeutral : EmbedColorHealthy;
    }

    private static bool IsAttentionNeeded(Order order, DateTime today)
    {
        return IsOverdue(order, today) ||
            order.Status == OrderStatus.Delayed ||
            !HasTracking(order);
    }

    private static bool IsOverdue(Order order, DateTime today)
    {
        return order.ExpectedDate.HasValue && order.ExpectedDate.Value.Date < today;
    }

    private static bool HasTracking(Order order)
    {
        return order.TrackingNumbers.Any(entry => !string.IsNullOrWhiteSpace(entry.Number));
    }

    private static bool IsFinalStatus(OrderStatus status)
    {
        return status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned;
    }

    private static bool IsOpenOrder(Order order)
    {
        return !IsFinalStatus(order.Status);
    }

    private static string FormatOrderReference(Order order)
    {
        var merchant = FormatEnum(order.Merchant);
        var orderNumber = CleanSingleLine(order.OrderNumber);
        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            return Truncate($"{merchant} #{orderNumber}", 56);
        }

        var item = CleanSingleLine(order.PrimaryItem);
        if (!string.IsNullOrWhiteSpace(item))
        {
            return Truncate($"{merchant} {item}", 56);
        }

        var account = CleanSingleLine(order.AccountEmail);
        if (!string.IsNullOrWhiteSpace(account))
        {
            return Truncate($"{merchant} {account}", 56);
        }

        return Truncate($"{merchant} order from {FormatDate(order.OrderDate)}", 56);
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

    private static string FormatNumber(int count)
    {
        return count.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static string FormatOrderCount(int count)
    {
        return count == 1 ? "1 order" : $"{FormatNumber(count)} orders";
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

    private static string CleanSingleLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        if (maxLength <= 3)
        {
            return value[..maxLength];
        }

        var contentLength = maxLength - 3;
        if (contentLength > 0 && char.IsHighSurrogate(value[contentLength - 1]))
        {
            contentLength--;
        }

        return string.Concat(value.AsSpan(0, contentLength), "...");
    }
}
