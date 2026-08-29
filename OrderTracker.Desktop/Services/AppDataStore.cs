using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public enum DataLoadStatus
{
    Loaded,
    Missing,
    Recovered,
    Failed
}

public sealed record DataLoadResult(
    AppData Data,
    DataLoadStatus Status,
    string? Message = null,
    string? BackupPath = null,
    int SkippedRows = 0,
    int SubstitutedValues = 0,
    bool IsReadOnly = false);

public sealed class AppDataStore
{
    public const int CurrentSchemaVersion = 1;

    private const int RetainedBackupCount = 5;
    private static readonly TimeSpan[] ReadRetryDelays =
    {
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000)
    };

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
            new SafeEnumJsonConverter<UiDensity>(UiDensity.Comfortable),
            new SafeEnumJsonConverter<OrderGroupOption>(OrderGroupOption.None),
            new SafeEnumJsonConverter<AccountGroupOption>(AccountGroupOption.None),
            new SafeEnumJsonConverter<ItemGroupOption>(ItemGroupOption.None),
            new SafeEnumJsonConverter<OrderSortOption>(OrderSortOption.NewestFirst),
            new SafeEnumJsonConverter<AccountSortOption>(AccountSortOption.NameAscending),
            new SafeEnumJsonConverter<ItemSortOption>(ItemSortOption.MostUsed),
            new SafeEnumJsonConverter<OrderStatus>(OrderStatus.Ordered),
            new SafeEnumJsonConverter<CarrierKind>(CarrierKind.Unknown),
            new SafeEnumJsonConverter<OrderAttentionFilter>(OrderAttentionFilter.All),
            new JsonStringEnumConverter()
        }
    };

    public string DataFilePath { get; } = AppPaths.DataFile;

    public DataLoadResult Load()
    {
        CleanUpOrphanedFiles();
        JsonReadDiagnostics.Reset();

        try
        {
            var json = ReadDataFileWithRetry();
            var legacyCreatedAt = File.GetLastWriteTime(DataFilePath);
            var data = JsonSerializer.Deserialize<AppData>(json, _jsonOptions)
                ?? throw new JsonException("Expected application data object.");
            NormalizeLoadedData(data, legacyCreatedAt);

            var skippedRows = JsonReadDiagnostics.SkippedElements;
            var substitutedValues = JsonReadDiagnostics.SubstitutedValues;
            var backupPath = skippedRows > 0 || substitutedValues > 0
                ? TryBackUpBeforeRepair()
                : null;
            var isNewerSchema = data.SchemaVersion > CurrentSchemaVersion;
            var message = isNewerSchema
                ? "This data file was written by a newer version of Order Tracker. Changes will not be saved."
                : null;

            return new DataLoadResult(
                data,
                DataLoadStatus.Loaded,
                message,
                backupPath,
                skippedRows,
                substitutedValues,
                isNewerSchema);
        }
        catch (FileNotFoundException)
        {
            return new DataLoadResult(CreateDefaultData(), DataLoadStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new DataLoadResult(CreateDefaultData(), DataLoadStatus.Missing);
        }
        catch (JsonException ex)
        {
            var backupPath = TryBackUpUnreadableDataFile();
            return new DataLoadResult(
                CreateDefaultData(),
                DataLoadStatus.Recovered,
                ex.Message,
                backupPath);
        }
        catch (Exception ex)
        {
            return new DataLoadResult(
                CreateDefaultData(),
                DataLoadStatus.Failed,
                ex.Message,
                IsReadOnly: true);
        }
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(AppPaths.RootFolder);
        data.SchemaVersion = CurrentSchemaVersion;

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        if (File.Exists(DataFilePath) && File.ReadAllBytes(DataFilePath).SequenceEqual(jsonBytes))
        {
            return;
        }

        var tempPath = $"{DataFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(jsonBytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(DataFilePath))
            {
                RotateBackups();
                try
                {
                    File.Replace(tempPath, DataFilePath, $"{DataFilePath}.bak", ignoreMetadataErrors: true);
                }
                catch
                {
                    File.Move(tempPath, DataFilePath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, DataFilePath);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private string ReadDataFileWithRetry()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(DataFilePath);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException &&
                ex is not FileNotFoundException &&
                ex is not DirectoryNotFoundException &&
                attempt < ReadRetryDelays.Length)
            {
                Thread.Sleep(ReadRetryDelays[attempt]);
            }
        }
    }

    private static void NormalizeLoadedData(AppData data, DateTime legacyCreatedAt)
    {
        if (data.SchemaVersion < 1)
        {
            data.SchemaVersion = 1;
        }

        data.Settings ??= new AppSettings();
        data.Settings.Columns ??= new ColumnSettings();
        if (data.Settings.UiExperienceVersion < 1)
        {
            data.Settings.ItemSort = ItemSortOption.MostUsed;
            data.Settings.UiExperienceVersion = 1;
        }

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

    private string? TryBackUpUnreadableDataFile()
    {
        if (!File.Exists(DataFilePath))
        {
            return null;
        }

        var backupPath = $"{DataFilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        try
        {
            File.Copy(DataFilePath, backupPath, overwrite: false);
            return backupPath;
        }
        catch
        {
            try
            {
                File.Move(DataFilePath, backupPath);
                return backupPath;
            }
            catch
            {
                return null;
            }
        }
    }

    private string? TryBackUpBeforeRepair()
    {
        try
        {
            var backupPath = Path.Combine(
                AppPaths.RootFolder,
                $"orders.pre-repair-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(DataFilePath, backupPath, overwrite: false);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }

    private void RotateBackups()
    {
        try
        {
            TryDeleteFile($"{DataFilePath}.bak.{RetainedBackupCount - 1}");
            for (var index = RetainedBackupCount - 2; index >= 0; index--)
            {
                var sourcePath = index == 0
                    ? $"{DataFilePath}.bak"
                    : $"{DataFilePath}.bak.{index}";
                var destinationPath = $"{DataFilePath}.bak.{index + 1}";
                if (File.Exists(sourcePath))
                {
                    File.Move(sourcePath, destinationPath, overwrite: true);
                }
            }
        }
        catch
        {
        }
    }

    private void CleanUpOrphanedFiles()
    {
        try
        {
            if (!Directory.Exists(AppPaths.RootFolder))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var tempPath in Directory.EnumerateFiles(AppPaths.RootFolder, "orders.json.*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(tempPath) < cutoff)
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }

            var brokenFiles = Directory.EnumerateFiles(AppPaths.RootFolder, "*.broken-*")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(RetainedBackupCount);
            foreach (var file in brokenFiles)
            {
                TryDeleteFile(file.FullName);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
