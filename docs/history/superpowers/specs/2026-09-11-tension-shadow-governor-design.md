# Tension Shadow Governor Design

**Status:** Approved architecture for an observational side-by-side model. Current AUTO remains the only authority.

## Goal

Run a second governor model beside current PowerFlow AUTO using the same telemetry so the user can compare governance behavior before allowing the new model to control anything.

## Product model

PowerFlow keeps its existing five named manual profiles: SAVER, BAL-E, BAL-P, PERF, and ULTRA. AUTO continues to actuate only SAVER through PERF. The seven-leg Machine Baseline remains a measurement protocol, not seven PowerFlow modes.

The new model exposes one continuous **Tension** value from 0 to 100. Tension describes how readily the governor is willing to trade efficiency for responsiveness:

- lower tension promotes later, qualifies upward transitions more slowly, sustains elevated behavior for less time, and settles toward efficiency sooner;
- higher tension promotes earlier, qualifies upward transitions faster, sustains elevated behavior longer, and settles more slowly;
- tension never overrides an application's maximum entitlement;
- tension never enables ULTRA for automatic governance.

Tension 50 is the neutral point. It preserves the learned envelope boundaries and the current entitlement timing, making Current AUTO and Tension Shadow directly comparable at the midpoint. Moving away from 50 changes the governance policy, not Windows settings directly.

Reference landmarks shown to the user are SAVER BIAS, BAL-E, BAL-P, and PERF BIAS. They are explanatory landmarks on the continuous control, not discrete mode buttons. ULTRA remains explicitly manual-only.

## Tension transform

Given a learned `OperatingEnvelope`, a base `PerformanceEntitlement`, and tension `t` clamped to 0..100:

- normalized position `p = t / 100`;
- envelope boundary shift is `10 - 20p` pressure points, so tension 0 delays promotions by +10 points, tension 50 makes no shift, and tension 100 advances promotions by -10 points;
- shifted boundaries preserve strict ordering and at least 5 pressure points between Eco, Efficient, Responsive, and 100%;
- qualification duration factor is `1.75 - 1.5p`, giving 1.0x at tension 50;
- lease duration factor is `0.6 + 0.8p`, giving 1.0x at tension 50;
- release hysteresis factor is `0.5 + 1.0p`, giving 1.0x at tension 50;
- maximum zone is inherited from the base entitlement and is never increased;
- learned power frontiers remain machine evidence and are not fabricated or shifted by tension.

The transform is deterministic and lives in Core. UI wording is derived separately.

## Runtime architecture

Current path remains unchanged:

`telemetry -> AdaptiveGovernorRuntime -> EnvelopeGovernor -> EnvelopeActuationPolicy -> PowerFlow profile -> Windows`

Shadow path is parallel and observational:

`same telemetry -> TensionShadowRuntime -> GovernorTensionPolicy -> independent EnvelopeGovernor -> TensionShadowEvaluation -> UI only`

The shadow runtime owns its own `EnvelopeGovernor` so qualification, lease, and release state are independent from Current AUTO. It evaluates each new activity sample once. It continues observing while manual/game authority is active because it has no authority of its own.

There is no shadow actuation method. `TensionShadowRuntime` must not depend on `PowerModeProfileRuntime`, `IPowerPlanController`, `PowerFlowController.ApplyAdaptiveGovernorDecisionAsync`, or `EnvelopeActuationPolicy`.

## Configuration

`PowerFlowConfig` gains nullable persisted `GovernorTensionPercent`. `EffectiveGovernorTensionPercent` clamps to 0..100 and defaults to 50 when absent, preserving compatibility with existing config files.

Changing the slider updates only the persisted tension value and the shadow model. It does not toggle Auto, latch a mode, or apply a profile.

## Live UI

The existing Machine Envelope card becomes a compact **GOVERNOR MODELS** comparison while retaining approximately the same vertical footprint:

- header: model confidence;
- Current AUTO row: current model zone plus actually applied PowerFlow profile;
- Tension Shadow row: tension value, shadow allowed zone, and the PowerFlow profile that would correspond to that zone;
- one 0..100 Tension slider with Relaxed and Responsive ends plus four reference landmarks;
- shadow language always says `SHADOW`, `WOULD`, `ESTIMATE`, or equivalent; it never implies application.

The current actor card remains separate.

## Timeline overlay

The pressure lane continues to show the current learned/current policy context. Tension Shadow adds a visually subordinate dashed/ghost overlay for its effective Eco/Efficient/Responsive boundaries. Current policy stays visually primary.

The overlay updates immediately when tension moves. It must not alter telemetry points, current AUTO history, hover truth, or the applied-profile readout.

## Before/after meaning

Current AUTO is the "before/current" reference. Tension Shadow is the proposed/alternative envelope. The user can compare:

- zone boundaries;
- current allowed zone versus shadow allowed zone;
- current applied profile versus shadow would-use profile;
- qualification/sustain/settle behavior in concise outcome language.

If Machine Baseline contains measurements for the corresponding profile, the UI may label those facts as measured. Predictions for unmeasured tension positions must be labeled estimates. V1 does not synthesize interpolated watt or throughput numbers.

## Responsive behavior

The comparison must remain valid at the product's existing sizes. At Compact density, explanatory detail may collapse but both Current AUTO and Tension Shadow identities, decisions, and the tension control remain visible. Expanded and Full Screen may show the fuller explanation.

No new minimum window size is introduced. The existing 760x440 -> 900x560 continuous resize morph remains intact.

## Safety and acceptance

A candidate is acceptable only if:

1. current AUTO produces the same actuation results as before when shadow tension changes;
2. shadow evaluation cannot reach an actuator by type dependency or App wiring;
3. tension 50 resolves to the learned envelope and base timing;
4. low versus high tension demonstrably changes promotion boundaries and timing in the expected direction;
5. app entitlement ceilings remain authoritative;
6. the Live UI clearly distinguishes APPLIED from SHADOW/WOULD;
7. 760x440, 820x500, 860x520, 900x560, 1100x680, and 1280x800 have zero visible clipping;
8. Light, Dark, and Follow Windows remain readable;
9. full Core, Windows, App, Release, XAML, and diff-hygiene gates pass;
10. the currently published app is not replaced until the candidate has passed headless qualification and is ready for live A/B testing.
