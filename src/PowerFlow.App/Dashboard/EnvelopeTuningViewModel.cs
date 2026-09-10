using System.ComponentModel;
using System.Runtime.CompilerServices;
using PowerFlow.Core.Envelope;

namespace PowerFlow.App.Dashboard;

public sealed class EnvelopeTuningViewModel : INotifyPropertyChanged
{
    private OperatingEnvelope _learnedEnvelope = new(20, 50, 80, null, null, null);
    private PerformanceEntitlement _learnedEntitlement = PerformanceEntitlement.LegacyPerformance;
    private IReadOnlyList<OperatingObservation> _history = Array.Empty<OperatingObservation>();
    private AnalyticalSelection _selection = AnalyticalSelection.Empty;
    private EnvelopeTuningLayer _layer = EnvelopeTuningLayer.Learned;
    private double? _ecoCeilingPressure;
    private double? _efficientCeilingPressure;
    private double? _responsiveCeilingPressure;
    private EnvelopeZone? _maximumZone;
    private TimeSpan? _qualificationDuration;
    private TimeSpan? _leaseDuration;
    private TimeSpan? _releaseHysteresis;
    private EnvelopeZone? _manualOverrideZone;
    private CounterfactualReplayResult _replay = EmptyReplay();

    public event PropertyChangedEventHandler? PropertyChanged;

    public EnvelopeTuningLayer Layer => _layer;
    public string LayerLabel => _layer.ToString().ToUpperInvariant();
    public string LayerExplanation => _layer switch
    {
        EnvelopeTuningLayer.Learned => "Machine-derived learned baseline",
        EnvelopeTuningLayer.Tuned => "Your semantic tuning over the learned model",
        _ => "Explicit semantic override of the adaptive model"
    };

    public OperatingEnvelope LearnedEnvelope => _learnedEnvelope;
    public PerformanceEntitlement LearnedEntitlement => _learnedEntitlement;

    public EnvelopeTuning CandidateTuning => new(
        _layer,
        _ecoCeilingPressure,
        _efficientCeilingPressure,
        _responsiveCeilingPressure,
        _maximumZone,
        _qualificationDuration,
        _leaseDuration,
        _releaseHysteresis,
        _manualOverrideZone);

    public CounterfactualReplayResult Replay => _replay;
    public AnalyticalSelection Selection => _selection;
    public OperatingEnvelope CurrentEnvelope => CandidateTuning.ApplyTo(_learnedEnvelope);
    public PerformanceEntitlement CurrentEntitlement => CandidateTuning.ApplyTo(_learnedEntitlement);

    public string BoundarySummary
    {
        get
        {
            var envelope = CandidateTuning.ApplyTo(_learnedEnvelope);
            return $"Eco ≤ {envelope.EcoCeilingPressure:0.#}% · Efficient ≤ {envelope.EfficientCeilingPressure:0.#}% · Responsive ≤ {envelope.ResponsiveCeilingPressure:0.#}%";
        }
    }

    public string ActorSummary => _selection.Actor ?? (_selection.ObservationIndices.Count > 1 ? "Multiple actors / selected region" : "System / no selected actor");

    public string LeaseSummary
    {
        get
        {
            var entitlement = CandidateTuning.ApplyTo(_learnedEntitlement);
            return $"Qualify {entitlement.QualificationDuration.TotalSeconds:0.#}s · Lease {entitlement.LeaseDuration.TotalSeconds:0.#}s · Release {entitlement.ReleaseHysteresis.TotalSeconds:0.#}s";
        }
    }

    public string EntitlementSummary
    {
        get
        {
            var entitlement = CandidateTuning.ApplyTo(_learnedEntitlement);
            return $"Semantic ceiling: {entitlement.MaximumZone}";
        }
    }

    public string ReplaySummary => _replay.ObservationCount == 0
        ? "No retained observations to replay"
        : $"{_replay.ChangedDecisionCount} decisions changed across {_replay.ObservationCount} retained observations";

    public void Load(
        OperatingEnvelope learnedEnvelope,
        PerformanceEntitlement learnedEntitlement,
        IReadOnlyList<OperatingObservation> history,
        AnalyticalSelection selection)
    {
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(learnedEntitlement);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(selection);
        _learnedEnvelope = learnedEnvelope;
        _learnedEntitlement = learnedEntitlement;
        _history = history.ToArray();
        _selection = selection;
        ResetFields();
        Recompute();
    }

    public void ApplyCandidateTuning(EnvelopeTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        _layer = tuning.ManualOverrideZone is not null ? EnvelopeTuningLayer.Override : tuning.Layer;
        _ecoCeilingPressure = tuning.EcoCeilingPressure;
        _efficientCeilingPressure = tuning.EfficientCeilingPressure;
        _responsiveCeilingPressure = tuning.ResponsiveCeilingPressure;
        _maximumZone = tuning.MaximumZone;
        _qualificationDuration = tuning.QualificationDuration;
        _leaseDuration = tuning.LeaseDuration;
        _releaseHysteresis = tuning.ReleaseHysteresis;
        _manualOverrideZone = tuning.ManualOverrideZone;
        NormalizeBoundaryOverrides();
        Recompute();
    }
    public void RestorePersistedTuning(EnvelopeTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        _layer = tuning.ManualOverrideZone is not null ? EnvelopeTuningLayer.Override : tuning.Layer;
        _ecoCeilingPressure = tuning.EcoCeilingPressure;
        _efficientCeilingPressure = tuning.EfficientCeilingPressure;
        _responsiveCeilingPressure = tuning.ResponsiveCeilingPressure;
        _maximumZone = tuning.MaximumZone;
        _qualificationDuration = tuning.QualificationDuration;
        _leaseDuration = tuning.LeaseDuration;
        _releaseHysteresis = tuning.ReleaseHysteresis;
        _manualOverrideZone = tuning.ManualOverrideZone;
        NormalizeBoundaryOverrides();
        Recompute();
    }
    public void UpdateLearnedContext(
        OperatingEnvelope learnedEnvelope,
        PerformanceEntitlement learnedEntitlement,
        IReadOnlyList<OperatingObservation> history,
        AnalyticalSelection selection)
    {
        ArgumentNullException.ThrowIfNull(learnedEnvelope);
        ArgumentNullException.ThrowIfNull(learnedEntitlement);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(selection);
        _learnedEnvelope = learnedEnvelope;
        _learnedEntitlement = learnedEntitlement;
        _history = history.ToArray();
        _selection = selection;
        NormalizeBoundaryOverrides();
        Recompute();
    }

    private void NormalizeBoundaryOverrides()
    {
        var effectiveEco = _ecoCeilingPressure ?? _learnedEnvelope.EcoCeilingPressure;
        var effectiveResponsive = _responsiveCeilingPressure ?? _learnedEnvelope.ResponsiveCeilingPressure;
        if (effectiveEco >= effectiveResponsive - 1)
        {
            _ecoCeilingPressure = null;
            _efficientCeilingPressure = null;
            _responsiveCeilingPressure = null;
            return;
        }

        if (_efficientCeilingPressure is double efficient)
            _efficientCeilingPressure = Math.Clamp(efficient, effectiveEco + 1, effectiveResponsive - 1);
        var effectiveEfficient = _efficientCeilingPressure ?? _learnedEnvelope.EfficientCeilingPressure;
        if (_ecoCeilingPressure is double eco)
            _ecoCeilingPressure = Math.Clamp(eco, 0, effectiveEfficient - 1);
        if (_responsiveCeilingPressure is double responsive)
            _responsiveCeilingPressure = Math.Clamp(responsive, effectiveEfficient + 1, 100);
    }
    public void UpdateSelection(IReadOnlyList<OperatingObservation> history, AnalyticalSelection selection)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(selection);
        _history = history.ToArray();
        _selection = selection;
        Recompute();
    }

    public void SetEcoCeilingPressure(double value)
    {
        var current = CandidateTuning.ApplyTo(_learnedEnvelope);
        _ecoCeilingPressure = Math.Clamp(value, 0, current.EfficientCeilingPressure - 1);
        MarkTuned();
    }

    public void SetEfficientCeilingPressure(double value)
    {
        var current = CandidateTuning.ApplyTo(_learnedEnvelope);
        _efficientCeilingPressure = Math.Clamp(value, current.EcoCeilingPressure + 1, current.ResponsiveCeilingPressure - 1);
        MarkTuned();
    }

    public void SetResponsiveCeilingPressure(double value)
    {
        var current = CandidateTuning.ApplyTo(_learnedEnvelope);
        _responsiveCeilingPressure = Math.Clamp(value, current.EfficientCeilingPressure + 1, 100);
        MarkTuned();
    }

    public void SetMaximumZone(EnvelopeZone zone)
    {
        _maximumZone = zone;
        MarkTuned();
    }

    public void SetQualificationDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
        _qualificationDuration = value;
        MarkTuned();
    }

    public void SetLeaseDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
        _leaseDuration = value;
        MarkTuned();
    }

    public void SetReleaseHysteresis(TimeSpan value)
    {
        if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
        _releaseHysteresis = value;
        MarkTuned();
    }

    public void SetManualOverride(EnvelopeZone zone)
    {
        _manualOverrideZone = zone;
        _layer = EnvelopeTuningLayer.Override;
        Recompute();
    }

    public void ClearManualOverride()
    {
        _manualOverrideZone = null;
        _layer = HasTunedValues() ? EnvelopeTuningLayer.Tuned : EnvelopeTuningLayer.Learned;
        Recompute();
    }

    private bool HasTunedValues() =>
        _ecoCeilingPressure is not null || _efficientCeilingPressure is not null || _responsiveCeilingPressure is not null ||
        _maximumZone is not null || _qualificationDuration is not null || _leaseDuration is not null || _releaseHysteresis is not null;
    public void ResetToLearned()
    {
        ResetFields();
        Recompute();
    }

    private void MarkTuned()
    {
        if (_manualOverrideZone is null) _layer = EnvelopeTuningLayer.Tuned;
        Recompute();
    }

    private void ResetFields()
    {
        _layer = EnvelopeTuningLayer.Learned;
        _ecoCeilingPressure = null;
        _efficientCeilingPressure = null;
        _responsiveCeilingPressure = null;
        _maximumZone = null;
        _qualificationDuration = null;
        _leaseDuration = null;
        _releaseHysteresis = null;
        _manualOverrideZone = null;
    }

    private void Recompute()
    {
        _replay = CounterfactualReplay.Run(_history, _learnedEnvelope, _learnedEntitlement, CandidateTuning);
        RaiseAll();
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Layer), nameof(LayerLabel), nameof(LayerExplanation), nameof(CandidateTuning), nameof(Replay), nameof(Selection),
            nameof(BoundarySummary), nameof(ActorSummary), nameof(LeaseSummary), nameof(EntitlementSummary), nameof(ReplaySummary), nameof(CurrentEnvelope), nameof(CurrentEntitlement)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static CounterfactualReplayResult EmptyReplay() => new(
        0, 0,
        Enum.GetValues<EnvelopeZone>().ToDictionary(zone => zone, _ => 0),
        Enum.GetValues<EnvelopeZone>().ToDictionary(zone => zone, _ => 0),
        null, null);
}
