using PowerFlow.App.Controller;
using PowerFlow.Core.Policy;

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
    int? TotalCores = null);
