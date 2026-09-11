using PowerFlow.App.Controller;
using PowerFlow.Core.Profiling;
using Xunit;

namespace PowerFlow.App.Tests.Controller;

public sealed class MachineBaselineSessionRuntimeTests
{
    [Fact]
    public async Task Completed_run_is_persisted_and_exposed_after_session_finishes()
    {
        var run = SampleRun();
        var runner = new FakeRunner(run);
        MachineBaselineComparisonRun? saved = null;
        var session = new MachineBaselineSessionRuntime(runner, value => { saved = value; return Task.CompletedTask; });

        await session.StartAsync(MachineBaselineSchedule.Standard);

        Assert.False(session.Snapshot.IsRunning);
        Assert.Equal(run.Id, session.Snapshot.LastCompletedRun?.Id);
        Assert.Equal(run.Id, saved?.Id);
        Assert.Null(session.Snapshot.Error);
    }

    [Fact]
    public async Task Cancel_and_wait_cancels_active_run_without_losing_restoration_path()
    {
        var runner = new BlockingRunner();
        var session = new MachineBaselineSessionRuntime(runner, _ => Task.CompletedTask);
        var running = session.StartAsync(MachineBaselineSchedule.Standard);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await session.CancelAndWaitAsync();
        await running;

        Assert.True(runner.SawCancellation);
        Assert.False(session.Snapshot.IsRunning);
        Assert.Equal("Cancelled", session.Snapshot.Status);
    }

    private static MachineBaselineComparisonRun SampleRun()
    {
        var profile = CpuCapabilityAnalysis.Build(DateTimeOffset.UtcNow, "BAL-E", "sig", new[] { new CpuCapabilityPoint(1, 10, 20, 100, 4) });
        var result = new MachineBaselineModeResult(MachineBaselineMode.BalancedEfficient, "BAL-E", "sig", new MachineIdleSummary(TimeSpan.FromSeconds(90), 50, 55, 100, 4, null, 0, 1.25, 90), profile);
        var results = new[] { result };
        return new MachineBaselineComparisonRun(Guid.NewGuid(), DateTimeOffset.UtcNow, MachineBaselineSchedule.Standard, results, MachineBaselineAnalysis.Recommend(results));
    }

    private sealed class FakeRunner(MachineBaselineComparisonRun run) : IMachineBaselineRunner
    {
        public Task<MachineBaselineComparisonRun> RunAsync(MachineBaselineSchedule? schedule = null, IProgress<MachineBaselineProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(run);
    }

    private sealed class BlockingRunner : IMachineBaselineRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SawCancellation { get; private set; }

        public async Task<MachineBaselineComparisonRun> RunAsync(MachineBaselineSchedule? schedule = null, IProgress<MachineBaselineProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { SawCancellation = true; throw; }
            throw new InvalidOperationException();
        }
    }
}