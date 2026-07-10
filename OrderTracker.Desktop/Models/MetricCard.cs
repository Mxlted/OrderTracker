namespace OrderTracker.Desktop.Models;

public sealed class MetricCard
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Accent { get; set; } = "#5CC8FF";
}

public sealed class ChartPoint
{
    public string Label { get; set; } = string.Empty;

    public decimal Value { get; set; }

    public double Percent { get; set; }

    public string DisplayValue { get; set; } = string.Empty;

    public string Accent { get; set; } = "#5CC8FF";
}

public sealed class MonthlyComparisonPoint
{
    public string Label { get; set; } = string.Empty;

    public decimal Spend { get; set; }

    public decimal ProjectedRoi { get; set; }

    public double SpendPercent { get; set; }

    public double RoiPercent { get; set; }

    public string SpendDisplay { get; set; } = string.Empty;

    public string RoiDisplay { get; set; } = string.Empty;
}
