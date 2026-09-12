namespace PowerFlow.App.Dashboard;

public enum MotionMaterial
{
    Rigid,
    Compliant,
    Fluid
}

public readonly record struct MotionSample(double Progress, double Velocity, double SecondaryScale);