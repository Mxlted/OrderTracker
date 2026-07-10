using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class AppDataStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new OrderJsonConverter(),
            new MerchantKindJsonConverter(),
            new SafeEnumJsonConverter<BrowserPreference>(BrowserPreference.Default),
            new SafeEnumJsonConverter<AppTheme>(AppTheme.Dark),
            new SafeEnumJsonConverter<OrderGroupOption>(OrderGroupOption.None),
            new SafeEnumJsonConverter<AccountGroupOption>(AccountGroupOption.None),
            new SafeEnumJsonConverter<ItemGroupOption>(ItemGroupOption.None),
            new SafeEnumJsonConverter<OrderSortOption>(OrderSortOption.NewestFirst),
            new SafeEnumJsonConverter<AccountSortOption>(AccountSortOption.NameAscending),
            new SafeEnumJsonConverter<ItemSortOption>(ItemSortOption.NameAscending),
            new SafeEnumJsonConverter<OrderStatus>(OrderStatus.Ordered),
            new SafeEnumJsonConverter<CarrierKind>(CarrierKind.Unknown),
            new JsonStringEnumConverter()
        }
    };

    public string DataFilePath { get; }

    public AppDataStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "OrderTrackerDesktop");
        DataFilePath = Path.Combine(folder, "orders.json");
    }

    public AppData Load()
    {
        if (!File.Exists(DataFilePath))
        {
            return CreateDefaultData();
        }

        try
        {
            var json = File.ReadAllText(DataFilePath);
            var legacyCreatedAt = File.GetLastWriteTime(DataFilePath);
            var data = JsonSerializer.Deserialize<AppData>(json, _jsonOptions) ?? CreateDefaultData();
            NormalizeLoadedData(data, legacyCreatedAt);
            return data;
        }
        catch
        {
            TryBackUpUnreadableDataFile();
            return CreateDefaultData();
        }
    }

    public void Save(AppData data)
    {
        var folder = Path.GetDirectoryName(DataFilePath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var tempPath = $"{DataFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, DataFilePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }
    }

    private static void NormalizeLoadedData(AppData data, DateTime legacyCreatedAt)
    {
        data.Settings ??= new AppSettings();
        data.Settings.Columns ??= new ColumnSettings();
        NormalizeMerchantProjectedRoiPercents(data.Settings);
        data.Orders ??= new();
        data.AccountPresets ??= new();
        data.ItemPresets ??= new();

        data.Orders = new ObservableCollection<Order>(data.Orders.OfType<Order>());
        data.AccountPresets = new ObservableCollection<AccountPreset>(data.AccountPresets.OfType<AccountPreset>());
        data.ItemPresets = new ObservableCollection<ItemPreset>(data.ItemPresets.OfType<ItemPreset>());

        foreach (var order in data.Orders)
        {
            EnsureId(order);
            EnsureCreatedAt(order, legacyCreatedAt);
            order.NormalizeItemCollection();
            order.TrackingNumbers ??= new();
            order.TrackingNumbers = new ObservableCollection<TrackingEntry>(order.TrackingNumbers.OfType<TrackingEntry>());
            CarrierRecognizer.ApplyRecognition(order);
        }

        foreach (var preset in data.AccountPresets)
        {
            EnsureId(preset);
            EnsureCreatedAt(preset, legacyCreatedAt);
        }

        foreach (var preset in data.ItemPresets)
        {
            EnsureId(preset);
        }
    }

    private static void NormalizeMerchantProjectedRoiPercents(AppSettings settings)
    {
        var existing = settings.MerchantProjectedRoiPercents?
            .OfType<MerchantRoiSetting>()
            .GroupBy(setting => setting.Merchant)
            .ToDictionary(group => group.Key, group => Math.Max(0m, group.First().ProjectedRoiPercent))
            ?? new Dictionary<MerchantKind, decimal>();

        settings.MerchantProjectedRoiPercents = new ObservableCollection<MerchantRoiSetting>(
            AppSettings.ListedMerchants
                .Select(merchant => new MerchantRoiSetting
                {
                    Merchant = merchant,
                    ProjectedRoiPercent = existing.TryGetValue(merchant, out var percent)
                        ? percent
                        : AppSettings.DefaultProjectedRoiPercent
                }));
    }

    private static void EnsureId(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.Id))
        {
            order.Id = Guid.NewGuid().ToString("N");
        }
    }

    private static void EnsureId(AccountPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id))
        {
            preset.Id = Guid.NewGuid().ToString("N");
        }
    }

    private static void EnsureId(ItemPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id))
        {
            preset.Id = Guid.NewGuid().ToString("N");
        }
    }

    private static AppData CreateDefaultData()
    {
        var data = new AppData
        {
            ItemPresets =
            {
                new ItemPreset
                {
                    Name = "Replacement item",
                    Category = "General",
                    DefaultQuantity = 1,
                    MerchantHint = MerchantKind.Unknown,
                    Notes = "Edit or delete this starter preset."
                }
            }
        };

        NormalizeLoadedData(data, DateTime.Now);
        return data;
    }

    private static void EnsureCreatedAt(Order order, DateTime legacyOrderCreatedAt)
    {
        if (order.CreatedAt <= DateTime.MinValue.AddDays(1))
        {
            order.CreatedAt = legacyOrderCreatedAt;
        }
    }

    private static void EnsureCreatedAt(AccountPreset preset, DateTime legacyCreatedAt)
    {
        if (preset.CreatedAt <= DateTime.MinValue.AddDays(1))
        {
            preset.CreatedAt = legacyCreatedAt;
        }
    }

    private void TryBackUpUnreadableDataFile()
    {
        try
        {
            if (!File.Exists(DataFilePath))
            {
                return;
            }

            var backupPath = $"{DataFilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            File.Copy(DataFilePath, backupPath, overwrite: false);
        }
        catch
        {
            // Loading should still recover even if backup creation fails.
        }
    }

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // A stale temp file is less harmful than hiding the original save error.
        }
    }
}
