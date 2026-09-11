namespace PowerFlow.Windows.Activity;

public sealed record LogicalProcessorTelemetry(
    int LogicalProcessorIndex,
    int PhysicalCoreIndex,
    bool IsParked,
    double? UtilizationPercent = null,
    double? FrequencyMhz = null,
    double? PercentOfMaximumFrequency = null,
    double? ProcessorPerformancePercent = null);

public sealed record SystemMetricsSnapshot(
    double? MemoryUsedPercent,
    string? MachineName,
    int? ActiveCores = null,
    int? TotalCores = null,
    IReadOnlyList<LogicalProcessorTelemetry>? LogicalProcessors = null,
    double? ProcessorQueueLength = null);

public interface ISystemMetricsProvider
{
    SystemMetricsSnapshot Read();
}