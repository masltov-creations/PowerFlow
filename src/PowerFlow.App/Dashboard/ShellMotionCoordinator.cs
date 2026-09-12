using Windows.Graphics;

namespace PowerFlow.App.Dashboard;

public readonly record struct ShellMotionFrame(RectInt32 Bounds, MotionSample Sample, MotionSample ChildSample, bool IsComplete);

public sealed class ShellMotionCoordinator
{
    private RectInt32 _start;
    private RectInt32 _target;
    private MotionMaterial _material;
    private TimeSpan _duration;
    private DateTimeOffset _startedAt;
    private double _initialVelocity;
    private double _childInitialVelocity;
    private bool _active;

    public void Begin(RectInt32 start, RectInt32 target, MotionMaterial material, TimeSpan duration, DateTimeOffset at, double initialVelocity = 0d)
    {
        if (material == MotionMaterial.Compliant)
            throw new ArgumentOutOfRangeException(nameof(material), material, "Native window bounds must use a monotonic material.");
        BeginCore(start, target, material, duration, at, initialVelocity, initialVelocity);
    }

    public ShellMotionFrame Sample(DateTimeOffset at)
    {
        if (!_active) return RestingFrame(_target);
        if (_duration == TimeSpan.Zero) return Complete();
        var elapsed = Math.Max(0d, (at - _startedAt).TotalMilliseconds);
        var t = Math.Clamp(elapsed / _duration.TotalMilliseconds, 0d, 1d);
        if (t >= 1d) return Complete();
        var sample = PhysicalMotionCurve.Sample(_material, t, _initialVelocity);
        var childSample = PhysicalMotionCurve.Sample(MotionMaterial.Compliant, t, _childInitialVelocity);
        var bounds = ShellTransitionGeometry.Interpolate(_start, _target, sample.Progress);
        return new ShellMotionFrame(bounds, sample, childSample, false);
    }

    public void Retarget(RectInt32 target, MotionMaterial material, TimeSpan duration, DateTimeOffset at)
    {
        if (material == MotionMaterial.Compliant)
            throw new ArgumentOutOfRangeException(nameof(material), material, "Native window bounds must use a monotonic material.");
        var current = Sample(at);
        BeginCore(current.Bounds, target, material, duration, at, current.Sample.Velocity, current.ChildSample.Velocity);
    }

    private void BeginCore(RectInt32 start, RectInt32 target, MotionMaterial material, TimeSpan duration, DateTimeOffset at, double initialVelocity, double childInitialVelocity)
    {
        _start = start;
        _target = target;
        _material = material;
        _duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _startedAt = at;
        _initialVelocity = initialVelocity;
        _childInitialVelocity = childInitialVelocity;
        _active = true;
    }

    private ShellMotionFrame Complete()
    {
        _active = false;
        return RestingFrame(_target);
    }

    private static ShellMotionFrame RestingFrame(RectInt32 bounds)
    {
        var rest = new MotionSample(1d, 0d, 1d);
        return new ShellMotionFrame(bounds, rest, rest, true);
    }
}