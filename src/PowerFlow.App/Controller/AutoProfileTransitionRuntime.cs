using PowerFlow.Core.Envelope;
using PowerFlow.Core.Policy;

namespace PowerFlow.App.Controller;

public sealed record AutoProfileTransitionDecision(
    PowerFlowOperatingProfile Profile,
    bool ShouldApply,
    string Reason);

public sealed class AutoProfileTransitionRuntime
{
    private static readonly TimeSpan StrongBounceWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BounceWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StableLowerModeWindow = TimeSpan.FromSeconds(60);
    private const int MaxResidencyMultiplier = 8;

    private readonly Dictionary<PowerFlowOperatingMode, int> _residencyMultipliers = new();
    private PowerFlowOperatingMode? _trackedMode;
    private DateTimeOffset _enteredAt;
    private PowerFlowOperatingMode? _pendingPromotionMode;
    private DateTimeOffset? _pendingPromotionSince;
    private PowerFlowOperatingMode? _pendingDownshiftMode;
    private DateTimeOffset? _pendingDownshiftSince;
    private PowerFlowOperatingMode? _lastDownshiftFrom;
    private PowerFlowOperatingMode? _lastDownshiftTo;
    private DateTimeOffset? _lastDownshiftAt;

    public AutoProfileTransitionDecision Evaluate(
        EnvelopeZone effectiveZone,
        PowerState controllerState,
        PowerFlowOperatingProfile? currentProfile,
        DateTimeOffset now,
        TimeSpan downshiftQualification)
    {
        if (downshiftQualification < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(downshiftQualification));
        var desired = AlignWithControllerState(PowerFlowOperatingProfiles.ForAutoZone(effectiveZone), controllerState);
        Synchronize(currentProfile, now);
        RelaxBackoffAfterStableLowerMode(currentProfile, now);

        if (currentProfile is null)
            return new(desired, true, $"Auto initializes {desired.Mode} aligned with Windows {controllerState}.");

        if (desired.Mode == currentProfile.Mode)
        {
            ClearPendingPromotion();
            ClearPendingDownshift();
            return new(currentProfile, false, $"Auto remains in {currentProfile.Mode}.");
        }

        var currentRank = Rank(currentProfile.Mode);
        var desiredRank = Rank(desired.Mode);
        if (desiredRank > currentRank)
        {
            ClearPendingDownshift();
            if (_pendingPromotionMode != desired.Mode || _pendingPromotionSince is null)
            {
                _pendingPromotionMode = desired.Mode;
                _pendingPromotionSince = now;
            }

            var required = TimeSpan.FromSeconds(Math.Max(0d, desired.PromotionQualificationSeconds));
            var elapsed = now - _pendingPromotionSince.Value;
            if (elapsed < required)
            {
                return new(currentProfile, false,
                    $"Auto qualifying {desired.Mode} for {required.TotalSeconds:0.##}s; {Math.Max(0d, elapsed.TotalSeconds):0.##}s observed.");
            }

            return new(desired, true, $"Auto promotion to {desired.Mode} qualified after {elapsed.TotalSeconds:0.##}s.");
        }

        ClearPendingPromotion();
        if (currentProfile.WindowsState != controllerState)
        {
            ClearPendingDownshift();
            return new(desired, true, $"Windows state changed to {controllerState}; align processor profile to {desired.Mode}.");
        }

        var next = desiredRank < currentRank - 1
            ? PowerFlowOperatingProfiles.For((PowerFlowOperatingMode)(currentRank - 1))
            : desired;
        if (_pendingDownshiftMode != next.Mode || _pendingDownshiftSince is null)
        {
            _pendingDownshiftMode = next.Mode;
            _pendingDownshiftSince = now;
        }

        var multiplier = ResidencyMultiplier(currentProfile.Mode);
        var minimumResidency = TimeSpan.FromSeconds(Math.Max(0d, currentProfile.MinimumResidencySeconds) * multiplier);
        var scaledQualificationTicks = Math.Min(TimeSpan.MaxValue.Ticks, downshiftQualification.Ticks * (double)multiplier);
        var requiredDownshiftQualification = TimeSpan.FromTicks((long)Math.Round(scaledQualificationTicks, MidpointRounding.AwayFromZero));
        var residency = now - _enteredAt;
        var downshiftElapsed = now - _pendingDownshiftSince.Value;
        if (residency < minimumResidency || downshiftElapsed < requiredDownshiftQualification)
        {
            return new(currentProfile, false,
                $"Auto holds {currentProfile.Mode}; residency {Math.Max(0d, residency.TotalSeconds):0.#}/{minimumResidency.TotalSeconds:0}s and lower-demand qualification {Math.Max(0d, downshiftElapsed.TotalSeconds):0.#}/{requiredDownshiftQualification.TotalSeconds:0.#}s.");
        }

        return new(next, true, $"Auto downshift from {currentProfile.Mode} to {next.Mode} after sustained lower demand and stable residency.");
    }

    public void Commit(PowerFlowOperatingProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var previous = _trackedMode;
        if (previous is PowerFlowOperatingMode previousMode && previousMode != profile.Mode)
        {
            if (Rank(profile.Mode) < Rank(previousMode))
            {
                _lastDownshiftFrom = previousMode;
                _lastDownshiftTo = profile.Mode;
                _lastDownshiftAt = now;
            }
            else if (Rank(profile.Mode) > Rank(previousMode)
                && _lastDownshiftFrom == profile.Mode
                && _lastDownshiftTo == previousMode
                && _lastDownshiftAt is DateTimeOffset downshiftAt
                && now - downshiftAt <= BounceWindow)
            {
                var rebound = now - downshiftAt;
                var current = ResidencyMultiplier(profile.Mode);
                _residencyMultipliers[profile.Mode] = rebound <= StrongBounceWindow
                    ? MaxResidencyMultiplier
                    : Math.Min(MaxResidencyMultiplier, current * 2);
            }
        }

        _trackedMode = profile.Mode;
        _enteredAt = now;
        ClearPendingPromotion();
        ClearPendingDownshift();
    }

    public void Reset()
    {
        _trackedMode = null;
        _enteredAt = default;
        _residencyMultipliers.Clear();
        _lastDownshiftFrom = null;
        _lastDownshiftTo = null;
        _lastDownshiftAt = null;
        ClearPendingPromotion();
        ClearPendingDownshift();
    }

    private void Synchronize(PowerFlowOperatingProfile? currentProfile, DateTimeOffset now)
    {
        if (currentProfile is null)
        {
            _trackedMode = null;
            return;
        }
        if (_trackedMode == currentProfile.Mode) return;
        _trackedMode = currentProfile.Mode;
        _enteredAt = now;
        ClearPendingPromotion();
        ClearPendingDownshift();
    }

    private void RelaxBackoffAfterStableLowerMode(PowerFlowOperatingProfile? currentProfile, DateTimeOffset now)
    {
        if (currentProfile is null || _lastDownshiftTo != currentProfile.Mode || _lastDownshiftFrom is not PowerFlowOperatingMode higher || _lastDownshiftAt is not DateTimeOffset at)
            return;
        if (now - at < StableLowerModeWindow) return;

        var current = ResidencyMultiplier(higher);
        if (current > 1) _residencyMultipliers[higher] = Math.Max(1, current / 2);
        _lastDownshiftFrom = null;
        _lastDownshiftTo = null;
        _lastDownshiftAt = null;
    }

    private int ResidencyMultiplier(PowerFlowOperatingMode mode) =>
        _residencyMultipliers.TryGetValue(mode, out var multiplier) ? Math.Clamp(multiplier, 1, MaxResidencyMultiplier) : 1;

    private static PowerFlowOperatingProfile AlignWithControllerState(PowerFlowOperatingProfile desired, PowerState controllerState) =>
        controllerState switch
        {
            PowerState.PowerSaver => PowerFlowOperatingProfiles.Saver,
            PowerState.Balanced when desired.WindowsState == PowerState.PowerSaver => PowerFlowOperatingProfiles.Balanced,
            PowerState.Balanced when desired.WindowsState == PowerState.HighPerformance => PowerFlowOperatingProfiles.Performance,
            _ => desired
        };

    private static int Rank(PowerFlowOperatingMode mode) => (int)mode;

    private void ClearPendingPromotion()
    {
        _pendingPromotionMode = null;
        _pendingPromotionSince = null;
    }

    private void ClearPendingDownshift()
    {
        _pendingDownshiftMode = null;
        _pendingDownshiftSince = null;
    }
}