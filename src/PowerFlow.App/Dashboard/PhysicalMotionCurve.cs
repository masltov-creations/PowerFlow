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
        var (progress, velocity) = MinimumJerk(t);
        ApplyMomentum(t, initialVelocity, ref progress, ref velocity);
        return new MotionSample(Math.Clamp(progress, 0d, 1d), velocity, 1d);
    }

    private static MotionSample Compliant(double t, double initialVelocity)
    {
        // Let semantic/layout disclosure trail the native window very slightly without rubber-band overshoot.
        var followT = Math.Clamp(t - .05d * Math.Sin(Math.PI * t), 0d, 1d);
        var (progress, followVelocity) = MinimumJerk(followT);
        var followDerivative = 1d - .05d * Math.PI * Math.Cos(Math.PI * t);
        var velocity = followVelocity * followDerivative;
        ApplyMomentum(t, initialVelocity, ref progress, ref velocity);
        return new MotionSample(Math.Clamp(progress, 0d, 1d), velocity, 1d);
    }

    private static (double Progress, double Velocity) MinimumJerk(double t)
    {
        var x = Math.Clamp(t, 0d, 1d);
        var x2 = x * x;
        var x3 = x2 * x;
        var progress = x3 * (10d + x * (-15d + 6d * x));
        var velocity = 30d * x2 * (1d - x) * (1d - x);
        return (progress, velocity);
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