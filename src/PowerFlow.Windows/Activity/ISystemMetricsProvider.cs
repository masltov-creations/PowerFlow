namespace PowerFlow.Windows.Activity;

public sealed record SystemMetricsSnapshot(double? MemoryUsedPercent, string? MachineName);

public interface ISystemMetricsProvider
{
    SystemMetricsSnapshot Read();
}
