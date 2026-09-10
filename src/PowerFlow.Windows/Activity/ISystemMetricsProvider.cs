namespace PowerFlow.Windows.Activity;

public sealed record SystemMetricsSnapshot(
    double? MemoryUsedPercent,
    string? MachineName,
    int? ActiveCores = null,
    int? TotalCores = null);

public interface ISystemMetricsProvider
{
    SystemMetricsSnapshot Read();
}
