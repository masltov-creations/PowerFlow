namespace PowerFlow.Windows.Power;

public interface IPowerPlanController
{
    Task<IReadOnlyList<PowerPlanInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<PowerPlanInfo> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<PowerPlanSwitchResult> ActivateAsync(Guid schemeId, CancellationToken cancellationToken = default);
}

public interface IPowerPlanNative
{
    IReadOnlyList<PowerPlanInfo> EnumeratePlans();
    Guid GetActiveScheme();
    int SetActiveScheme(Guid id);
}
