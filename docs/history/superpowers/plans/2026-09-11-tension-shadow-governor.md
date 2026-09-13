# Tension Shadow Governor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persisted, adjustable Tension Shadow governor that evaluates beside Current AUTO, visualizes its alternative envelope and would-use profile, and has no actuation path.

**Architecture:** Add a deterministic Core tension transform, a stateful App shadow runtime with its own `EnvelopeGovernor`, App wiring that stores but never actuates its latest evaluation, and a compact Live comparison/ghost overlay. Current AUTO code remains the sole actuation owner.

**Tech Stack:** .NET 8, C#, WinUI 3, xUnit, existing PowerFlow Core/App architecture.

**Spec:** `docs/superpowers/specs/2026-09-11-tension-shadow-governor-design.md`

## Global Constraints

- Current AUTO remains the only authority and only path to `EnvelopeActuationPolicy`, `PowerFlowOperatingProfileRuntime`, `PowerFlowController.ApplyAdaptiveGovernorDecisionAsync`, and Windows power policy.
- Tension range is 0..100; absent persisted values default to 50.
- Tension 50 is behaviorally neutral relative to the learned envelope and base entitlement timing.
- Shadow maximum zone never exceeds the base/app entitlement maximum.
- ULTRA remains manual-only.
- No new minimum window size; 760x440 remains valid.
- Keep the published PowerFlow process running until the isolated candidate is fully headless-qualified.

---

### Task 1: Core tension policy

**Files:**
- Create: `src/PowerFlow.Core/Envelope/GovernorTensionPolicy.cs`
- Test: `tests/PowerFlow.Core.Tests/Envelope/GovernorTensionPolicyTests.cs`

**Interfaces:**
- Consumes: `OperatingEnvelope`, `PerformanceEntitlement`.
- Produces: `GovernorTensionPolicy.Resolve(double, OperatingEnvelope, PerformanceEntitlement)` returning `GovernorTensionModel` with clamped tension, reference landmark, effective envelope, and effective entitlement.

- [ ] **Step 1: Write failing tests** for neutral 50, low/high directionality, ordering/minimum gaps, entitlement ceiling preservation, and clamping.
- [ ] **Step 2: Run** `dotnet test tests/PowerFlow.Core.Tests/PowerFlow.Core.Tests.csproj -c Release --filter GovernorTensionPolicyTests` and verify RED because the policy types do not exist.
- [ ] **Step 3: Implement** the exact transform from the spec. Use a helper that shifts boundaries together then clamps them while preserving >=5 point gaps; do not change power frontiers.
- [ ] **Step 4: Re-run the focused tests** and require GREEN.
- [ ] **Step 5: Commit** `Add governor tension policy`.

### Task 2: Stateful non-actuating shadow runtime

**Files:**
- Create: `src/PowerFlow.App/Controller/TensionShadowRuntime.cs`
- Modify: `src/PowerFlow.App/Controller/AdaptiveGovernorRuntime.cs` only if a small entitlement-resolution helper can be shared without changing current behavior.
- Test: `tests/PowerFlow.App.Tests/Controller/TensionShadowRuntimeTests.cs`

**Interfaces:**
- Consumes: `ControllerSnapshot`, continuity history, `PowerFlowConfig`, tension.
- Produces: `TensionShadowEvaluation` containing the shadow decision, learned/effective envelopes, effective entitlement, confidence, actor, reference landmark, and `PowerFlowOperatingMode WouldUseMode` derived with `PowerFlowOperatingProfiles.ForAutoZone`.

- [ ] **Step 1: Write failing tests** proving each activity sample is evaluated once, manual/game latch does not stop observation, app entitlement ceiling is preserved, tension 50 matches neutral behavior, and low/high tension diverge under the same telemetry.
- [ ] **Step 2: Add a source-contract test** proving `TensionShadowRuntime.cs` contains no references to `EnvelopeActuationPolicy`, `PowerModeProfileRuntime`, `ApplyAdaptiveGovernorDecisionAsync`, or `IPowerPlanController`.
- [ ] **Step 3: Run focused App tests** and verify RED.
- [ ] **Step 4: Implement** the runtime with its own `EnvelopeGovernor`; reuse calibration and actor-entitlement semantics but do not share governor state.
- [ ] **Step 5: Run focused tests** and require GREEN.
- [ ] **Step 6: Commit** `Add observational tension shadow runtime`.

### Task 3: Persisted tension and App wiring

**Files:**
- Modify: `src/PowerFlow.Core/Rules/PowerFlowConfig.cs`
- Modify: `src/PowerFlow.App/App.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Test: `tests/PowerFlow.Core.Tests/Rules/PowerFlowConfigTests.cs` or nearest existing config test file.
- Test: `tests/PowerFlow.App.Tests/Controller/AdaptiveGovernorRuntimeTests.cs` for wiring/source contracts.

**Interfaces:**
- Produces: `PowerFlowConfig.EffectiveGovernorTensionPercent` and a `Func<TensionShadowEvaluation?>` provider passed into `MainWindow`.

- [ ] **Step 1: Write failing config tests** for absent=50, clamp 0/100, and serialization-compatible nullable field.
- [ ] **Step 2: Write failing App source-contract tests** asserting `OnSnapshotChanged` evaluates both runtimes, only `AdaptiveGovernorRuntimeEvaluation` reaches `ApplyAdaptiveGovernorEvaluationAsync`, and the shadow provider is passed to MainWindow.
- [ ] **Step 3: Run focused tests** and verify RED.
- [ ] **Step 4: Implement config and App wiring.** Store the latest shadow evaluation; never call an actuator with it.
- [ ] **Step 5: Run focused tests** and require GREEN.
- [ ] **Step 6: Commit** `Wire persisted shadow governor`.

### Task 4: Dashboard comparison projection

**Files:**
- Create: `src/PowerFlow.App/Dashboard/GovernorComparisonProjection.cs`
- Modify: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs` only where needed to expose current decision truth.
- Test: `tests/PowerFlow.App.Tests/Dashboard/GovernorComparisonProjectionTests.cs`

**Interfaces:**
- Consumes: current decision/profile, shadow evaluation, tension, optional latest Machine Baseline run.
- Produces: compact strings/state for Current AUTO and Tension Shadow plus behavioral descriptions for scale-up, sustain, and settle.

- [ ] **Step 1: Write failing projection tests** requiring explicit `APPLIED` versus `SHADOW / WOULD USE` wording, reference-landmark labels, neutral/directional behavior text, and no fabricated baseline numbers when a tension position is unmeasured.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement pure projection code** with no WinUI dependency.
- [ ] **Step 4: Run focused tests** and require GREEN.
- [ ] **Step 5: Commit** `Add governor comparison projection`.

### Task 5: Ghost envelope overlay

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/PerformanceTimelinePolicyOverlayTests.cs` or create `TensionShadowOverlayTests.cs` if no focused overlay file exists.

**Interfaces:**
- Add: `SetShadowPolicyContext(OperatingEnvelope? envelope)`.
- Current `SetPolicyContext(...)` remains authoritative/primary.

- [ ] **Step 1: Write failing tests/source contracts** requiring a separate shadow overlay state, dashed/subordinate rendering, and no mutation of `_data`, current policy context, hover observations, or policy-drag state.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Implement** three ghost boundary rails on the CPU-pressure lane using the shadow envelope boundaries; clear them when shadow is unavailable.
- [ ] **Step 4: Run focused tests** and require GREEN.
- [ ] **Step 5: Commit** `Overlay tension shadow envelope`.

### Task 6: Live Tension control and side-by-side card

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/CurrentProductSurfaceTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ResponsiveShellTests.cs`

**Interfaces:**
- UI: Current AUTO row, Tension Shadow row, 0..100 slider, Relaxed/Responsive endpoints, four reference landmarks.
- Slider handler updates `_config = _config with { GovernorTensionPercent = value }` through the existing `_applyConfig` path; it never calls mode/profile application.

- [ ] **Step 1: Write failing source/UI contracts** for explicit Current AUTO and Tension Shadow labels, slider range/default, no slider handler reference to `_applyOperatingMode` or profile runtime, and compact visibility.
- [ ] **Step 2: Run focused tests** and verify RED.
- [ ] **Step 3: Replace the Machine Envelope card content** without materially increasing its compact vertical footprint; wire projection text, shadow overlay, and slider config update.
- [ ] **Step 4: Ensure reduced-motion and theme code paths are unchanged.**
- [ ] **Step 5: Run focused tests** and require GREEN.
- [ ] **Step 6: Commit** `Show current and tension governors side by side`.

### Task 7: Definitive qualification and live A/B candidate

**Files:**
- Modify: `docs/acceptance/reference-host-acceptance-2026-09-11.md` only after live evidence exists.

**Interfaces:**
- Candidate remains isolated until all headless gates pass.

- [ ] **Step 1: Run full tests:** Core, Windows, App Release suites.
- [ ] **Step 2: Run** `dotnet build PowerFlow.sln -c Release`, source-XAML XML parse, and `git diff --check`; require zero warnings/errors and clean hygiene.
- [ ] **Step 3: Verify the published master app is still the only running production-path process before candidate swap.**
- [ ] **Step 4: Only after headless qualification, replace the running app with the isolated candidate for live acceptance.**
- [ ] **Step 5: Verify Tension 0/25/50/75/100 updates the SHADOW decision/ghost envelope while APPLIED state remains controlled by Current AUTO. Capture cases where the two models agree and disagree.
- [ ] **Step 6: Resize live through 760x440, 820x500, 860x520, 900x560, 1100x680, 1280x800; require exact native bounds, process responsiveness, and zero visible clipping.**
- [ ] **Step 7: Exercise Light, Dark, Follow Windows and restore the original theme.**
- [ ] **Step 8: Check Application/.NET/WER events and require no new PowerFlow crash events.**
- [ ] **Step 9: Record concise acceptance evidence, commit, fast-forward master, rerun definitive full gate on master, push, verify local/origin/fresh GitHub SHA equality, relaunch from default master path, and remove the feature worktree.**
