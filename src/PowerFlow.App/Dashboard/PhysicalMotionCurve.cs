namespace PowerFlow.App.Dashboard;

public static class PhysicalMotionCurve
{
    public static MotionSample Sample(MotionMaterial material, double normalizedTime, double initialVelocity = 0d)
    {
        var t = Math.Clamp(normalizedTime, 0d, 1d);
        if (t <= 0d) return new MotionSample(0d, initialVelocity, 1d);
        if (t >= 1d) return new MotionSample(1d, 0d, 1d);

        return material switch
        {
            MotionMaterial.Compliant => Compliant(t, initialVelocity),
            MotionMaterial.Fluid => Fluid(t, initialVelocity),
            _ => Rigid(t, initialVelocity)
        };
    }

    private static MotionSample Rigid(double t, double initialVelocity)
    {
        var (progress, velocity) = CriticalDamped(t, 9d);
        ApplyMomentum(t, initialVelocity, ref progress, ref velocity);
        return new MotionSample(Math.Clamp(progress, 0d, 1d), velocity, 1d);
    }

    private static MotionSample Fluid(double t, double initialVelocity)
    {
        var (progress, velocity) = CriticalDamped(t, 7.5d);
        ApplyMomentum(t, initialVelocity, ref progress, ref velocity);
        var secondaryScale = 1d + .026d * Math.Sin(2d * Math.PI * t) * Math.Sin(Math.PI * t);
        return new MotionSample(Math.Clamp(progress, 0d, 1d), velocity, Math.Clamp(secondaryScale, .97d, 1.03d));
    }

    private static MotionSample Compliant(double t, double initialVelocity)
    {
        const double dampingRatio = .78d;
        const double naturalFrequency = 8d;
        var root = Math.Sqrt(1d - dampingRatio * dampingRatio);
        var dampedFrequency = naturalFrequency * root;
        var decay = Math.Exp(-dampingRatio * naturalFrequency * t);
        var progress = 1d - decay *
            (Math.Cos(dampedFrequency * t) + dampingRatio / root * Math.Sin(dampedFrequency * t));
        var velocity = naturalFrequency / root * decay * Math.Sin(dampedFrequency * t);
        ApplyMomentum(t, initialVelocity, ref progress, ref velocity);
        progress = Math.Clamp(progress, 0d, 1.025d);
        return new MotionSample(progress, velocity, 1d);
    }

    private static (double Progress, double Velocity) CriticalDamped(double t, double omega)
    {
        var normalization = 1d - Math.Exp(-omega) * (1d + omega);
        var decay = Math.Exp(-omega * t);
        var progress = (1d - decay * (1d + omega * t)) / normalization;
        var velocity = omega * omega * t * decay / normalization;
        return (progress, velocity);
    }

    private static void ApplyMomentum(double t, double initialVelocity, ref double progress, ref double velocity)
    {
        var v0 = Math.Clamp(initialVelocity, -2d, 2d);
        if (Math.Abs(v0) < 1e-9) return;
        const double decayRate = 7d;
        const double strength = .08d;
        var decay = Math.Exp(-decayRate * t);
        var envelope = t * (1d - t);
        var momentum = strength * v0 * envelope * decay;
        progress += momentum;
        var envelopeDerivative = 1d - 2d * t;
        velocity += strength * v0 * decay * (envelopeDerivative - decayRate * envelope);
    }
}