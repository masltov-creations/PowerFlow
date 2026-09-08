namespace PowerFlow.Windows.Power;

public sealed class ActivePowerPlanChangedEventArgs(Guid schemeId) : EventArgs
{
    public Guid SchemeId { get; } = schemeId;
}

public interface IActivePowerPlanObserver : IDisposable
{
    event EventHandler<ActivePowerPlanChangedEventArgs>? ActivePlanChanged;
    void Start();
    void Stop();
}
