# Governor tension model v1

Status: proposed product direction; not current runtime behavior.

## Intent

PowerFlow should explain CPU governance as an understandable performance envelope, not as a collection of Windows processor-policy numbers.

The user-facing concept is **governor tension**: how readily PowerFlow is allowed to trade efficiency for responsiveness and scale when work demands it.

The existing PowerFlow profiles remain useful, but become named reference presets on this model rather than unrelated recipes:

- SAVER
- BAL-E
- BAL-P
- PERF
- ULTRA

Auto continues to govern dynamically and may use SAVER through PERF. ULTRA remains an explicit manual maximum-performance choice unless that product rule is intentionally changed later.

The seven-leg Baseline remains a measurement protocol, not seven PowerFlow profiles. It compares Windows Power Saver, Windows Balanced, and the five PowerFlow profiles.

## What the model should communicate

A user should be able to understand, visually and without learning EPP/core-floor terminology:

- how much CPU capacity may be brought online;
- how readily the governor may favor responsiveness/boost;
- how aggressively it may scale upward under demand;
- how long higher-performance behavior may be sustained;
- how readily it returns toward efficiency when demand falls;
- which workload-importance classes are allowed to pull the governor upward;
- the current operating point inside the allowed envelope.

Windows power plans and processor-policy values remain implementation details underneath the model.

## Interaction model

The primary control should be one friendly **tension** control from relaxed/efficiency-biased toward aggressive/responsiveness-biased behavior.

Progressive disclosure may expose the behavioral dimensions behind that control, but should describe outcomes first. Raw Windows policy values are diagnostic/advanced detail, not the primary interaction.

When a user considers a change, PowerFlow should show:

1. the current governor envelope;
2. the proposed envelope as a ghost/overlay;
3. which behavioral dimensions expand or contract;
4. expected consequences derived from measured machine data where available;
5. a before/after comparison that does not imply certainty beyond the available baseline evidence.

The visual should make the relationship between **current demand**, **current applied policy**, **allowed envelope**, and **proposed envelope** obvious at a glance.

## Baseline integration

Machine Baseline should inform this model. Measured throughput, idle power, throughput/watt, and the throughput knee can anchor the preset positions and help explain what changing tension is likely to cost or gain on this specific machine.

The model must distinguish measured evidence from prediction. A predicted before/after view should be labeled as an estimate until that configuration has been measured.

## Design constraint

Do not expose a new bank of low-level tuning sliders under a friendlier name. The model should reduce cognitive load, preserve one behavioral owner, and make PowerFlow's governance legible rather than teaching users Windows power-policy internals.
