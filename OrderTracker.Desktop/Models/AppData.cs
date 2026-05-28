using System.Collections.ObjectModel;

namespace OrderTracker.Desktop.Models;

public sealed class AppData
{
    public ObservableCollection<Order> Orders { get; set; } = new();

    public ObservableCollection<ItemPreset> ItemPresets { get; set; } = new();

    public ObservableCollection<AccountPreset> AccountPresets { get; set; } = new();

    public AppSettings Settings { get; set; } = new();
}
