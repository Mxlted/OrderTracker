using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace OrderTracker.Desktop.Models;

public sealed class AppData
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    public ObservableCollection<Order> Orders { get; set; } = new();

    public ObservableCollection<ItemPreset> ItemPresets { get; set; } = new();

    public ObservableCollection<AccountPreset> AccountPresets { get; set; } = new();

    public AppSettings Settings { get; set; } = new();
}
