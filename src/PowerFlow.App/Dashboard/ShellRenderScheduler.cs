namespace PowerFlow.App.Dashboard;

public sealed class ShellRenderScheduler
{
    private readonly TimeSpan _settleDelay;
    private ShellLogicalSize? _pendingFrame;
    private DateTimeOffset? _lastSampleAt;
    private bool _commitPending;

    public ShellRenderScheduler(TimeSpan settleDelay)
    {
        if (settleDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(settleDelay));
        _settleDelay = settleDelay;
    }

    public void SubmitSize(ShellLogicalSize size, DateTimeOffset at)
    {
        _pendingFrame = size;
        _lastSampleAt = at;
        _commitPending = true;
    }

    public ShellLogicalSize? ConsumePendingFrame()
    {
        var pending = _pendingFrame;
        _pendingFrame = null;
        return pending;
    }

    public bool ShouldCommit(DateTimeOffset at)
        => _commitPending && _lastSampleAt is DateTimeOffset last && at - last >= _settleDelay;

    public void MarkCommitted() => _commitPending = false;
}