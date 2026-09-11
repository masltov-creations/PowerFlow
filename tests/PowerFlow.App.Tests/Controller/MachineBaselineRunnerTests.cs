using PowerFlow.App.Controller;
using PowerFlow.Core.Profiling;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class MachineBaselineRunnerTests
{
    [Fact]
    public async Task Standard_run_enters_all_six_modes_in_order_and_restores_once()
    {
        var host = new FakeHost();
        var profiler = new FakeProfiler();
        var delays = new List<TimeSpan>();
        var runner = new MachineBaselineRunner(host, profiler, (duration, _) => { delays.Add(duration); return Task.CompletedTask; });

        var run = await runner.RunAsync(MachineBaselineSchedule.Standard);

        Assert.Equal(MachineBaselineSchedule.Standard.Modes, host.EnteredModes);
        Assert.Equal(1, host.CaptureCount);
        Assert.Equal(1, host.RestoreCount);
        Assert.Equal(6, run.Results.Count);
        Assert.All(profiler.Options, options => Assert.Equal(TimeSpan.FromSeconds(39), options.PointDuration));
        Assert.Equal(6, delays.Count(duration => duration == TimeSpan.FromSeconds(15)));
        Assert.Equal(MachineBaselineAnalysis.Recommend(run.Results), run.Recommendation);
    }

    [Fact]
    public async Task Benchmark_failure_still_restores_original_machine_state()
    {
        var host = new FakeHost();
        var profiler = new FakeProfiler(failOnCall: 3);
        var runner = new MachineBaselineRunner(host, profiler, (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(MachineBaselineSchedule.Standard));

        Assert.Equal(1, host.RestoreCount);
        Assert.Equal(3, host.EnteredModes.Count);
    }

    [Fact]
    public async Task Cancellation_still_restores_original_machine_state()
    {
        var host = new FakeHost();
        var profiler = new FakeProfiler(cancelOnCall: 2);
        var runner = new MachineBaselineRunner(host, profiler, (_, _) => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(MachineBaselineSchedule.Standard));

        Assert.Equal(1, host.RestoreCount);
    }

    private sealed class FakeHost : IMachineBaselineHost
    {
        public List<MachineBaselineMode> EnteredModes { get; } = [];
        public int CaptureCount { get; private set; }
        public int RestoreCount { get; private set; }

        public Task<MachineBaselineRestorePoint> CaptureRestorePointAsync(CancellationToken cancellationToken)
        {
            CaptureCount++;
            return Task.FromResult(new MachineBaselineRestorePoint("before"));
        }

        public Task EnterModeAsync(MachineBaselineMode mode, CancellationToken cancellationToken)
        {
            EnteredModes.Add(mode);
            return Task.CompletedTask;
        }

        public Task<MachineIdleSummary> CaptureIdleAsync(TimeSpan duration, CancellationToken cancellationToken)
            => Task.FromResult(new MachineIdleSummary(duration, 60, 65, 110, 4.1, 12, 0, 1.5, 90));

        public Task RestoreAsync(MachineBaselineRestorePoint restorePoint, CancellationToken cancellationToken)
        {
            RestoreCount++;
            return Task.CompletedTask;
        }

        public string Label(MachineBaselineMode mode) => mode.ToString();
    }

    private sealed class FakeProfiler(int failOnCall = -1, int cancelOnCall = -1) : ICpuCapabilityProfiler
    {
        private int _calls;
        public List<CpuCapabilityProfilerOptions> Options { get; } = [];

        public Task<CpuCapabilityProfile> RunAsync(CpuCapabilityProfilerOptions options, IProgress<CpuCapabilityProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            _calls++;
            Options.Add(options);
            if (_calls == failOnCall) throw new InvalidOperationException("benchmark failed");
            if (_calls == cancelOnCall) throw new OperationCanceledException(cancellationToken);
            var points = new[]
            {
                new CpuCapabilityPoint(1, 20 + _calls, 50, 110, 4.1),
                new CpuCapabilityPoint(16, 100 + _calls, 90, 120, 4.3)
            };
            return Task.FromResult(CpuCapabilityAnalysis.Build(DateTimeOffset.UtcNow, $"mode-{_calls}", $"sig-{_calls}", points));
        }
    }

    [Fact]
    public void Mode_routes_keep_native_windows_profiles_separate_from_powerflow_overlays()
    {
        var saver = MachineBaselineModeRoutes.For(MachineBaselineMode.WindowsSaver);
        var windowsBalanced = MachineBaselineModeRoutes.For(MachineBaselineMode.WindowsBalanced);
        var balE = MachineBaselineModeRoutes.For(MachineBaselineMode.BalancedEfficient);
        var balP = MachineBaselineModeRoutes.For(MachineBaselineMode.BalancedPerformance);
        var perf = MachineBaselineModeRoutes.For(MachineBaselineMode.Performance);
        var auto = MachineBaselineModeRoutes.For(MachineBaselineMode.Auto);

        Assert.Equal(PowerFlow.Core.Policy.PowerState.PowerSaver, saver.NativeState);
        Assert.Null(saver.PowerFlowMode);
        Assert.Equal(PowerFlow.Core.Policy.PowerState.Balanced, windowsBalanced.NativeState);
        Assert.Null(windowsBalanced.PowerFlowMode);
        Assert.Equal(PowerFlowOperatingMode.Balanced, balE.PowerFlowMode);
        Assert.Equal(PowerFlowOperatingMode.BalancedPerformance, balP.PowerFlowMode);
        Assert.Equal(PowerFlowOperatingMode.Performance, perf.PowerFlowMode);
        Assert.True(auto.IsAuto);
    }
}
