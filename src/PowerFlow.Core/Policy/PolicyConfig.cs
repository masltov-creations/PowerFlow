namespace PowerFlow.Core.Policy;

public sealed record PolicyConfig(
    PowerState RestingState,
    double CpuPromotionThresholdPercent,
    TimeSpan CpuPromotionWindow,
    double QuietThresholdPercent,
    TimeSpan QuietWindow,
    TimeSpan PostGameCooldown);
