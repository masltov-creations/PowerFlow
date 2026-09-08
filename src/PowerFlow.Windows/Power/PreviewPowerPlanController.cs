namespace PowerFlow.Windows.Power;

public sealed class PreviewPowerPlanController : IPowerPlanController
{
    private readonly IPowerPlanController _inner;
    private readonly object _gate = new();
    private Guid? _virtualActive;

    public PreviewPowerPlanController(IPowerPlanController inner) => _inner = inner;

    public Task<IReadOnlyList<PowerPlanInfo>> ListAsync(CancellationToken cancellationToken = default)
        => _inner.ListAsync(cancellationToken);

    public async Task<PowerPlanInfo> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        Guid? virtualActive;
        lock (_gate) virtualActive = _virtualActive;
        if (virtualActive is null) return await _inner.GetActiveAsync(cancellationToken);
        var plans = await _inner.ListAsync(cancellationToken);
        return plans.FirstOrDefault(p => p.Id == virtualActive.Value)
            ?? new PowerPlanInfo(virtualActive.Value, virtualActive.Value.ToString());
    }

    public async Task<PowerPlanSwitchResult> ActivateAsync(Guid schemeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plans = await _inner.ListAsync(cancellationToken);
        if (!plans.Any(p => p.Id == schemeId))
            return new PowerPlanSwitchResult(false, schemeId, (await GetActiveAsync(cancellationToken)).Id, "Preview plan does not exist.");
        lock (_gate) _virtualActive = schemeId;
        return new PowerPlanSwitchResult(true, schemeId, schemeId, null);
    }
}