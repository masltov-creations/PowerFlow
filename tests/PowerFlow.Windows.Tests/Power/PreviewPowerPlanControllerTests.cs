using PowerFlow.Windows.Power;
using Xunit;

namespace PowerFlow.Windows.Tests.Power;

public sealed class PreviewPowerPlanControllerTests
{
    [Fact]
    public async Task ActivateAsync_ChangesOnlyVirtualPlanAndNeverCallsWindowsActivator()
    {
        var inner = new FakePlans(PowerPlanIds.Balanced);
        var preview = new PreviewPowerPlanController(inner);

        var result = await preview.ActivateAsync(PowerPlanIds.PowerSaver);
        var observed = await preview.GetActiveAsync();

        Assert.True(result.Success);
        Assert.Equal(PowerPlanIds.PowerSaver, observed.Id);
        Assert.Equal(PowerPlanIds.Balanced, inner.Active);
        Assert.Empty(inner.Activations);
    }

    private sealed class FakePlans(Guid active) : IPowerPlanController
    {
        public Guid Active { get; private set; } = active;
        public List<Guid> Activations { get; } = [];
        public Task<IReadOnlyList<PowerPlanInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PowerPlanInfo>>([
            new(PowerPlanIds.PowerSaver, "Power saver"), new(PowerPlanIds.Balanced, "Balanced"), new(PowerPlanIds.HighPerformance, "High performance")]);
        public Task<PowerPlanInfo> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PowerPlanInfo(Active, "Balanced"));
        public Task<PowerPlanSwitchResult> ActivateAsync(Guid schemeId, CancellationToken cancellationToken = default)
        {
            Activations.Add(schemeId);
            Active = schemeId;
            return Task.FromResult(new PowerPlanSwitchResult(true, schemeId, Active, null));
        }
    }
}