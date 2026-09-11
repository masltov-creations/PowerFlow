using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;
using PowerFlow.Windows.Activity;

namespace PowerFlow.App.Telemetry;

public sealed record ContinuitySample(
    DateTimeOffset At,
    double CpuPercent,
    double? PackageWatts,
    double? AverageMhz,
    PowerState State,
    string Reason,
    bool IsLatched,
    string? LatchType,
    double ThresholdProgress,
    string? TriggerApplication,
    int? ActiveCores = null,
    int? TotalCores = null,
    IReadOnlyList<LogicalProcessorTelemetry>? LogicalProcessors = null,
    double? ProcessorQueueLength = null,
    DemandPressureTelemetry? DemandPressure = null,
    double? ProcessorPerformancePercent = null);