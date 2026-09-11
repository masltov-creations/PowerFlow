# PowerFlow Current Architecture

Status: **canonical**
Updated: **2026-09-11**

## Runtime ownership

PowerFlow has one process and one policy owner. The controller collects low-cost activity state and manages manual/game latch precedence. The adaptive governor evaluates qualified AUTO transitions. A single profile runtime applies the processor-policy overlay that corresponds to the chosen PowerFlow profile.

The architecture intentionally separates **decision** from **actuation**:

1. `TelemetryContinuityRecorder` retains bounded recent machine evidence.
2. `AdaptiveGovernorRuntime` converts current demand plus learned envelope and app importance into a semantic zone decision.
3. `EnvelopeActuationPolicy` enforces confidence, entitlement, AUTO/manual authority, and latch precedence.
4. `PowerFlowOperatingProfiles.ForAutoZone` maps the qualified zone to SAVER/BAL-E/BAL-P/PERF.
5. `PowerFlowController` applies the Windows state transition and records controller state.
6. `PowerModeProfileRuntime` applies the profile-specific processor settings (core floor, EPP, boost policy) and verifies the write.

AUTO never maps Boost to raw Windows High Performance. PERF remains on Windows Balanced with the qualified 75/10/2 overlay. ULTRA is the only current profile that uses Windows High Performance and it is manual-only.

## Authority precedence

Highest to lowest:

1. explicit manual profile;
2. explicit legacy game latch retained only for backward compatibility with genuine legacy rules;
3. AUTO adaptive decision;
4. safe learned/default behavior when evidence is insufficient.

New Low/Normal/High app-importance rules are excluded from the legacy game-latch detector.

## Adaptive model

The adaptive envelope is an internal decision mechanism used by AUTO and explained on Live. It is not a separate user product.

The Live shell must not present projected envelope zones as applied power modes. Projected observations are labeled **MODEL ZONE**; the applied-profile readout comes from PowerModeProfileRuntime.CurrentProfile and is the authoritative SAVER / BAL-E / BAL-P / PERF / ULTRA state.

Current app startup canonicalizes legacy persisted adaptive tuning to learned/default behavior and unpauses learning. This prevents removed Tune Auto controls from leaving invisible policy behind. The config shape remains tolerant of legacy fields so older JSON can be read and migrated safely.

## Workload policy

`AppRule` retains legacy fields for configuration compatibility, but current UI writes explicit `AppImportance` and clears custom entitlements. `EffectiveEntitlement` derives the runtime ceiling/timing from Low/Normal/High.

Service-policy records remain deserializable for backward compatibility but are normalized away by the current app and have no production UI or actuator.

## Baseline architecture

`MachineBaselineComparison.Standard` is the sole standard schedule and contains seven fixed legs. `PowerFlowMachineBaselineHost` owns reversible profile transitions and exact processor-policy snapshot/restore. Historical persisted runs may still contain an older AUTO leg; the renderer can tolerate historical data, but new standard runs never schedule AUTO.

## UI architecture

`MainWindow` owns one morphing shell and four current sections: Live, Workloads, Baseline, Settings. Live uses `PerformanceTimelineControl` plus compact envelope/actor explanation. There is no production `PerformanceAtlasControl`, `EfficiencyCompareControl`, or Tune control region.

The same shell state is presented at glance, compact, expanded, and full-screen densities. Presentation changes must not create a second telemetry or policy path.

## Configuration boundary

User-editable durable settings are startup, theme, reduced motion, and app importance rules. Legacy threshold, plan-mapping, service-policy, and adaptive-tuning fields may remain in the serialized schema only for safe backward-compatible loading. They are not current UI/API concepts and must not regain product authority accidentally.

## Safety boundary

PowerFlow does not modify BIOS, voltage, fan, firmware, or arbitrary process scheduling. Windows power plan and processor-policy writes remain the safety boundary. Live UI work on reference-host follows `AGENTS.md`: headless by default unless the user explicitly authorizes visible UI in the current conversation.