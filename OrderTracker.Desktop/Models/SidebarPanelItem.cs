using System.Windows.Input;

namespace OrderTracker.Desktop.Models;

public sealed class SidebarPanelItem
{
    public string Label { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Accent { get; set; } = "#5CC8FF";

    public ICommand? Command { get; set; }

    public object? CommandParameter { get; set; }
}
