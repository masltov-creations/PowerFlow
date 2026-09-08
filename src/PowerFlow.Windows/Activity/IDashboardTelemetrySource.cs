namespace PowerFlow.Windows.Activity;

public interface IDashboardTelemetrySource : IDisposable
{
    DashboardTelemetry Read(DateTimeOffset at);
}
