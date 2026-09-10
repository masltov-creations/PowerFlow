namespace PowerFlow.Windows.Activity;

public sealed record ActivitySample(double CpuPercent, DateTimeOffset At, bool Valid, TimeSpan SampleDuration);

public interface IActivitySource
{
    ActivitySample Sample(DateTimeOffset at);
}

public sealed record DashboardTelemetry(
    double? PackageWatts,
    double? AverageMhz,
    DateTimeOffset At,
    double? MemoryUsedPercent = null,
    string? MachineName = null);
