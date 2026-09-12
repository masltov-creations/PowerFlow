using Windows.Graphics;

namespace PowerFlow.App.Dashboard;

public readonly record struct ShellMotionFrame(RectInt32 Bounds, MotionSample Sample, bool IsComplete);

public sealed class ShellMotionCoordinator
{
    private RectInt32 _start;
    private RectInt32 _target;
    private MotionMaterial _material;
    private TimeSpan _duration;
    private DateTimeOffset _startedAt;
    private double _initialVelocity;
    private bool _active;

    public void Begin(RectInt32 start, RectInt32 target, MotionMaterial material, TimeSpan duration, DateTimeOffset at, double initialVelocity = 0d)
    {
        _start = start;
        _target = target;
        _material = material;
        _duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _startedAt = at;
        _initialVelocity = initialVelocity;
        _active = true;
    }

    public ShellMotionFrame Sample(DateTimeOffset at)
    {
        if (!_active) return new ShellMotionFrame(_target, new MotionSample(1d, 0d, 1d), true);
        if (_duration == TimeSpan.Zero) return Complete();
        var elapsed = Math.Max(0d, (at - _startedAt).TotalMilliseconds);
        var t = Math.Clamp(elapsed / _duration.TotalMilliseconds, 0d, 1d);
        if (t >= 1d) return Complete();
        var sample = PhysicalMotionCurve.Sample(_material, t, _initialVelocity);
        var bounds = ShellTransitionGeometry.Interpolate(_start, _target, sample.Progress);
        return new ShellMotionFrame(bounds, sample, false);
    }

    public void Retarget(RectInt32 target, MotionMaterial material, TimeSpan duration, DateTimeOffset at)
    {
        var current = Sample(at);
        Begin(current.Bounds, target, material, duration, at, current.Sample.Velocity);
    }

    private ShellMotionFrame Complete()
    {
        _active = false;
        return new ShellMotionFrame(_target, new MotionSample(1d, 0d, 1d), true);
    }
}