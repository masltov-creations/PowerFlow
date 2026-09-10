# PowerFlow Adaptive Performance Envelope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform PowerFlow from a named-power-plan cockpit into a learned, app-aware visual performance governor whose single instrument unfolds from tray Glance through Full Screen, with a synchronized KPI Timeline, adjustable Adaptive Performance Envelope, app entitlements, boost leases, and a linked multidimensional Performance Atlas.

**Architecture:** Preserve the existing single-HWND shell state/morph infrastructure and current Windows power-plan controller as the actuator boundary. Add a pure Core observation/envelope/entitlement model first, project it into App-layer view models, then replace the old trajectory/card presentation with one reusable analytical instrument whose density changes by shell state. The first implementation learns and simulates envelope decisions safely; existing plan actuation remains available behind a guarded adapter until the learned model has enough evidence and explicit policy to drive it.

**Tech Stack:** .NET 8, C#, WinUI 3/XAML, xUnit, existing PowerFlow Core/App/Windows projects, existing telemetry cadence and power-plan controller.

**Spec:** `docs/superpowers/specs/2026-09-09-powerflow-adaptive-performance-envelope-design.md`

## Global Constraints

- **reference-host UI Safety — HARD RULE:** Never launch PowerFlow UI, interact with the tray, use UI Automation, synthesize input, create/focus/activate visible windows, run preview/popup-preview/dashboard/fullscreen modes, or capture screenshots through surfaced UI on reference-host without fresh explicit user authorization for that exact live-UI action. `continue`, `proceed`, `build it`, or prior authorization do not count. All implementation and automated verification in this plan is headless.
- Preserve the existing single-HWND Hidden -> Glance -> Compact -> Expanded -> Full Screen shell mechanics; this plan changes the instrument inside the shell, not the ownership model.
- Do not use Qwen.
- Do not push, merge to master, tag, publish, or rewrite public screenshots without explicit user authorization.
- Do not invent telemetry. Unknown package power, clock, active-core count, attribution, learned benefit, or confidence renders as unavailable/low-confidence rather than fabricated values.
- No production code change without a failing test first. Every task must demonstrate RED then GREEN.
- No text below 11 px in product XAML.
- Raw Windows plan names and powercfg values are expert/actuator detail, not the primary user-facing abstraction.
- Default policy behavior must be conservative: learning/model uncertainty cannot silently grant more performance than the current controller would have allowed.

---

### Task 1: Introduce the shared operating-observation model

**Files:**
- Create: `src/PowerFlow.Core/Envelope/OperatingObservation.cs`
- Create: `src/PowerFlow.Core/Envelope/OperatingEnvelope.cs`
- Create: `src/PowerFlow.Core/Envelope/EnvelopeZone.cs`
- Create: `tests/PowerFlow.Core.Tests/Envelope/OperatingObservationTests.cs`
- Modify: `src/PowerFlow.App/Dashboard/DashboardInformation.cs`
- Modify: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/DashboardViewModelTests.cs`

**Interfaces:**
- Produces: `OperatingObservation(DateTimeOffset At, double CpuPressurePercent, double? PackageWatts, double? EffectiveClockMhz, int? ActiveCores, int? TotalCores, EnvelopeZone Zone, string? Actor, EnvelopeDecisionKind Decision)`.
- Produces: `EnvelopeZone { Eco, Efficient, Responsive, Boost }` and `EnvelopeDecisionKind { None, Brake, Qualifying, Lease }`.
- Produces: `OperatingEnvelope` with ordered zone boundaries expressed as semantic normalized pressure values and optional learned power frontiers.
- App view model exposes an immutable `IReadOnlyList<OperatingObservation> OperatingHistory` without starting a new timer.

- [ ] **Step 1: Write failing Core tests** proving observation validation/clamping, nullable telemetry preservation, zone ordering, and unknown telemetry behavior.
- [ ] **Step 2: Run** `dotnet test tests/PowerFlow.Core.Tests/PowerFlow.Core.Tests.csproj -c Release --filter FullyQualifiedName~Envelope` and verify RED because the Envelope types do not exist.
- [ ] **Step 3: Implement the minimal Core records/enums** with no Windows dependencies and no learning logic yet.
- [ ] **Step 4: Run the focused Core tests and verify GREEN.**
- [ ] **Step 5: Write failing App tests** proving each existing dashboard telemetry sample is projected into `OperatingHistory`, preserving timestamp/CPU/watts/clock/state and leaving unavailable core/actor values null.
- [ ] **Step 6: Run the focused App tests and verify RED.**
- [ ] **Step 7: Extend `DashboardViewModel` minimally** to maintain bounded operating history from its existing telemetry update path; do not add a sampler/timer.
- [ ] **Step 8: Run focused App tests and verify GREEN.**
- [ ] **Step 9: Commit** `feat: add PowerFlow operating observation model`.

### Task 2: Learn a machine-specific Adaptive Performance Envelope from observations

**Files:**
- Create: `src/PowerFlow.Core/Envelope/EnvelopeCalibration.cs`
- Create: `src/PowerFlow.Core/Envelope/EnvelopeCalibrationResult.cs`
- Create: `src/PowerFlow.Core/Envelope/EnvelopeConfidence.cs`
- Create: `tests/PowerFlow.Core.Tests/Envelope/EnvelopeCalibrationTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<OperatingObservation>`.
- Produces: `EnvelopeCalibrationResult` containing `OperatingEnvelope Envelope`, `EnvelopeConfidence Confidence`, sample counts, and optional learned sustained-efficiency frontier.
- Calibration is deterministic and pure. It does not change Windows settings or execute workloads.
- Initial algorithm uses robust buckets/medians over observed package power and clock/CPU response; if evidence is insufficient, returns defaults with Low confidence rather than pretending to know a knee.

- [ ] **Step 1: Write failing tests** for insufficient evidence, monotonic zone boundaries, outlier resistance, distinct sustained-workload clusters, and confidence increases with coverage.
- [ ] **Step 2: Run focused Core tests and verify RED.**
- [ ] **Step 3: Implement minimal deterministic passive calibration** using bounded quantile/median helpers in the same file; no ML dependency.
- [ ] **Step 4: Run focused tests and verify GREEN.**
- [ ] **Step 5: Add property/invariant tests** that randomized valid observations never produce non-monotonic zones or NaN/Infinity frontiers.
- [ ] **Step 6: Run full Core tests.**
- [ ] **Step 7: Commit** `feat: learn adaptive performance envelope`.

### Task 3: Add app performance entitlements and boost-lease decisions in dry-run form

**Files:**
- Create: `src/PowerFlow.Core/Envelope/PerformanceEntitlement.cs`
- Create: `src/PowerFlow.Core/Envelope/BoostLease.cs`
- Create: `src/PowerFlow.Core/Envelope/EnvelopeGovernor.cs`
- Create: `src/PowerFlow.Core/Envelope/GovernorDecision.cs`
- Create: `tests/PowerFlow.Core.Tests/Envelope/EnvelopeGovernorTests.cs`
- Modify: `src/PowerFlow.Core/Rules/AppRule.cs`
- Modify: `src/PowerFlow.Core/Rules/PowerFlowConfig.cs`
- Modify: `tests/PowerFlow.Core.Tests/Rules/ConfigTests.cs`
- Modify: `tests/PowerFlow.Windows.Tests/Configuration/JsonConfigStoreTests.cs`

**Interfaces:**
- `PerformanceEntitlement` defines maximum zone, qualification duration, lease duration, release hysteresis, and follow-children behavior.
- `EnvelopeGovernor.Evaluate(observation, envelope, entitlement, now)` returns `GovernorDecision` with requested zone, allowed zone, decision kind, qualification progress, lease expiry, explanation, and confidence.
- Existing `AppRuleMode` remains readable for schema compatibility; config migration maps Balanced -> Efficient ceiling and Performance -> Boost ceiling.
- No call to `WindowsPowerPlanController` in this task.

- [ ] **Step 1: Write failing governor tests** for brake-at-ceiling, qualification accumulation, temporary lease grant, lease renewal, release hysteresis, manual override precedence, and uncertainty-conservative behavior.
- [ ] **Step 2: Run focused Core tests and verify RED.**
- [ ] **Step 3: Implement the minimal pure governor state machine.**
- [ ] **Step 4: Run focused Core tests and verify GREEN.**
- [ ] **Step 5: Write failing config serialization/migration tests** for old rules and new entitlements.
- [ ] **Step 6: Run Core + Windows config tests and verify RED.**
- [ ] **Step 7: Add backward-compatible config fields/migration.** Preserve schema-1 input behavior and defaults.
- [ ] **Step 8: Run Core + Windows config tests and verify GREEN.**
- [ ] **Step 9: Commit** `feat: add app entitlements and boost leases`.

### Task 4: Build the synchronized multi-KPI Timeline projection

**Files:**
- Create: `src/PowerFlow.App/Dashboard/PerformanceTimelineProjection.cs`
- Create: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/PerformanceTimelineProjectionTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/PerformanceTimelineContractTests.cs`
- Modify: `src/PowerFlow.App/PowerFlow.App.csproj` only if explicit XAML inclusion is required by the project pattern.

**Interfaces:**
- Consumes: `IReadOnlyList<OperatingObservation>`, selected time window, `OperatingEnvelope`, and optional selected timestamp/range.
- Projection produces aligned lanes for CPU Pressure, Package Power, Effective Clock, Active Cores, plus Envelope/Decision event bands on one normalized X/time axis.
- Each lane has its own truthful Y-domain; optional normalized overlay is a separate projection mode and must be labeled `NORMALIZED`.
- XAML control exposes no timers and renders only supplied projection data.

- [ ] **Step 1: Write failing projection tests** for common X coordinates, lane-specific normalization, missing-data gaps, bounded time windows, nearest-sample cursor lookup, and decision/event markers.
- [ ] **Step 2: Run focused App tests and verify RED.**
- [ ] **Step 3: Implement the pure projection and verify focused tests GREEN.**
- [ ] **Step 4: Write failing source-contract tests** requiring four KPI lanes, one shared time canvas, cursor layer, envelope rail layer, actor/decision layer, 11px minimum labels, and no four independent time axes.
- [ ] **Step 5: Run contract tests and verify RED.**
- [ ] **Step 6: Implement `PerformanceTimelineControl`** with a compact WinUI Canvas/Path-based renderer reusing existing smooth-curve helpers where appropriate.
- [ ] **Step 7: Run contract tests and Release build; verify GREEN with zero build errors.**
- [ ] **Step 8: Commit** `feat: add synchronized PowerFlow timeline`.

### Task 5: Make the Timeline the single instrument that unfolds across shell density

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/ShellPresentationProfile.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/ShellPresentationProfileTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/AdaptiveShellEvolutionContractTests.cs`
- Modify/retire only after tests protect replacement: `TrajectoryControl.*`, `DecisionPressureControl.*`, `FlowFieldControl.*`, `RuleFlowControl.*`, `PowerModeControl.*` as warranted by actual references.

**Interfaces:**
- Glance: envelope + watts/GHz/cores summary, miniature timeline, actor, brake/lease, next action.
- Compact: short synchronized Timeline plus envelope/actor/decision and one high-value Efficiency <-> Responsiveness tuning surface.
- Expanded: Timeline dominates roughly upper two-thirds; narrow Live/Apps/Model/Tune navigation; contextual control region below.
- Full Screen: same Timeline remains; no separate dashboard architecture.
- Same shell instances/morph state continue to own the same HWND; this task must not alter tray/window ownership.

- [ ] **Step 1: Write failing shell-evolution/source-contract tests** for the canonical content at Glance/Compact/Expanded/Full and for removal of the old primary power-plan-card hierarchy.
- [ ] **Step 2: Run focused shell tests and verify RED.**
- [ ] **Step 3: Update semantic presentation profiles** so density governs Timeline detail, control depth, navigation, and contextual regions rather than named-plan cards.
- [ ] **Step 4: Replace the central cockpit composition with one `PerformanceTimelineControl` instance and density-aware surrounding regions.** Do not launch the app.
- [ ] **Step 5: Run focused shell/morph/readability/source tests and Release build; verify GREEN.**
- [ ] **Step 6: Remove only now-unreferenced obsolete experimental/dashboard controls and prove no live references remain with a source scan that treats ripgrep exit 1 as no-match success.
- [ ] **Step 7: Run full App tests again.**
- [ ] **Step 8: Commit** `feat: unfold adaptive timeline across PowerFlow shell`.

### Task 6: Add the multidimensional Performance Atlas

**Files:**
- Create: `src/PowerFlow.App/Dashboard/PerformanceAtlasProjection.cs`
- Create: `src/PowerFlow.App/Dashboard/PerformanceAtlasControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/PerformanceAtlasControl.xaml.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/PerformanceAtlasProjectionTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/PerformanceAtlasContractTests.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`

**Interfaces:**
- Atlas projects the same observations into X/Y selectable dimensions: PackagePower, EffectiveClock, ActiveCores, CpuPressure initially.
- Cell density = residency/sample density; optional cell value = efficiency/benefit only when evidence exists.
- Selection returns the exact observation/time indices represented by selected cells so Timeline and Atlas can cross-highlight without lossy reverse inference.
- Full Screen shows Timeline + Atlas concurrently. Expanded can morph central instrument Timeline <-> Atlas while preserving selection and time/app filters.

- [ ] **Step 1: Write failing projection tests** for deterministic bucketing, missing-value exclusion, stable density scaling, exact observation-index membership, and no invented efficiency values.
- [ ] **Step 2: Run focused tests and verify RED.**
- [ ] **Step 3: Implement pure Atlas projection and verify GREEN.**
- [ ] **Step 4: Write failing XAML/source-contract tests** for heatmap canvas, dimension selectors, learned-frontier overlay, selection layer, and Full Screen paired layout.
- [ ] **Step 5: Implement the control and shell integration without launching UI.**
- [ ] **Step 6: Run focused tests + Release build GREEN.**
- [ ] **Step 7: Commit** `feat: add linked PowerFlow performance atlas`.

### Task 7: Add shared selection, Tune controls, and counterfactual replay

**Files:**
- Create: `src/PowerFlow.Core/Envelope/EnvelopeTuning.cs`
- Create: `src/PowerFlow.Core/Envelope/CounterfactualReplay.cs`
- Create: `tests/PowerFlow.Core.Tests/Envelope/CounterfactualReplayTests.cs`
- Create: `src/PowerFlow.App/Dashboard/AnalyticalSelection.cs`
- Create: `src/PowerFlow.App/Dashboard/EnvelopeTuningViewModel.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/EnvelopeTuningViewModelTests.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.*`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceAtlasControl.*`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`

**Interfaces:**
- Tuning layers are `Learned`, `Tuned`, and `Override`; raw actuator values remain expert detail.
- Counterfactual replay takes retained observations + candidate tuning and returns changed decision counts/residency. Energy/performance deltas are nullable unless supported by actual measured evidence.
- Shared `AnalyticalSelection` carries selected observation indices/time range/actor across Timeline and Atlas.

- [ ] **Step 1: Write failing Core replay tests** for deterministic changed-decision counts, unchanged raw history, safe null estimates, and boundary tuning.
- [ ] **Step 2: Run focused Core tests and verify RED.**
- [ ] **Step 3: Implement tuning/replay minimally; verify GREEN.**
- [ ] **Step 4: Write failing App tests** for selection preservation Timeline -> Atlas -> Timeline, contextual machine/app/lease/boundary editing, and Learned/Tuned/Override copy.
- [ ] **Step 5: Implement the view model + binding seams and verify GREEN.**
- [ ] **Step 6: Add source-contract tests for draggable semantic rails plus accessible non-drag controls; implement both.**
- [ ] **Step 7: Run full Core/App tests + Release build.**
- [ ] **Step 8: Commit** `feat: add adaptive tuning and counterfactual replay`.

### Task 8: Connect truthful active-core telemetry and governor dry-run evidence

**Files:**
- Modify: `src/PowerFlow.Windows/Activity/ISystemMetricsProvider.cs`
- Modify: `src/PowerFlow.Windows/Activity/WindowsSystemMetricsProvider.cs`
- Modify: `src/PowerFlow.App/Telemetry/DashboardTelemetry.cs` or its actual definition file
- Modify: `src/PowerFlow.App/Telemetry/DashboardTelemetrySource.cs` or its actual definition file
- Modify: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs`
- Create/modify: `tests/PowerFlow.Windows.Tests/Activity/WindowsSystemMetricsProviderTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/DashboardViewModelTests.cs`

**Interfaces:**
- Add `ActiveCores`/`TotalCores` only if Windows can provide a lightweight, truthful observation at the existing telemetry cadence. If not reliably available without ETW/new high-overhead infrastructure, expose total cores and leave active cores unavailable for this release.
- Dashboard projects each observation through `EnvelopeGovernor` in dry-run mode and surfaces brake/qualifying/lease evidence without changing power plans.

- [ ] **Step 1: Write failing provider tests around a pure active-core calculation/parser seam if a reliable OS counter exists; otherwise write tests requiring null/unavailable semantics.**
- [ ] **Step 2: Verify RED.**
- [ ] **Step 3: Implement the lowest-overhead truthful provider path; no new continuous timer.**
- [ ] **Step 4: Verify Windows tests GREEN.**
- [ ] **Step 5: Write failing App tests for governor dry-run projection into operating history and explanation text.**
- [ ] **Step 6: Implement the dry-run integration; explicitly keep the existing actuator controller authoritative.**
- [ ] **Step 7: Run full Core + Windows + App tests and Release build.**
- [ ] **Step 8: Commit** `feat: feed live observations into adaptive governor`.

### Task 9: Guarded actuator bridge and backward-compatible app tuning

**Files:**
- Create: `src/PowerFlow.App/Controller/EnvelopeActuationPolicy.cs`
- Modify: `src/PowerFlow.App/Controller/PowerFlowController.cs`
- Modify: `src/PowerFlow.App/Settings/RulesPage.xaml`
- Modify: `src/PowerFlow.App/Settings/RulesPage.xaml.cs`
- Create: `tests/PowerFlow.App.Tests/Controller/EnvelopeActuationPolicyTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Controller/PowerFlowControllerTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/AppEntitlementPresentationTests.cs`

**Interfaces:**
- Learned governor is advisory by default. Actuation becomes eligible only when Auto is enabled, confidence/policy gates pass, no manual/game latch overrides it, and config explicitly enables adaptive actuation.
- Map semantic zones conservatively onto existing qualified Windows plans: Eco -> Power Saver, Efficient/Responsive -> Balanced unless explicitly calibrated otherwise, Boost -> High Performance. Do not invent per-process watt caps.
- Rules UI edits semantic entitlement ceiling/qualification/lease; Expert disclosure may show the underlying plan mapping.

- [ ] **Step 1: Write failing actuation-policy tests** proving advisory-default, confidence gate, manual/game precedence, app ceiling, and conservative zone-to-plan mapping.
- [ ] **Step 2: Verify RED.**
- [ ] **Step 3: Implement the pure guard/mapping policy and verify GREEN.**
- [ ] **Step 4: Write failing controller tests** showing no behavior change with adaptive actuation disabled and correct guarded behavior when explicitly enabled.
- [ ] **Step 5: Implement controller bridge minimally and verify GREEN.**
- [ ] **Step 6: Write failing Rules source/view-model tests for semantic entitlement editing; implement without removing backward schema support.**
- [ ] **Step 7: Run full App tests + Release build.**
- [ ] **Step 8: Commit** `feat: guard adaptive envelope actuation`.

### Task 10: Headless qualification and blocked visual gate

**Files:**
- Modify tests only if a genuine stale-contract failure is proven to conflict with the approved spec.
- Do not create screenshots or launch the app on reference-host without fresh explicit authorization.

**Interfaces:**
- Automated qualification is necessary but does not substitute for eventual human visual acceptance.

- [ ] **Step 1: Run full Core tests** in Release.
- [ ] **Step 2: Run full Windows tests** in Release.
- [ ] **Step 3: Run full App tests** in Release.
- [ ] **Step 4: Run exact Release build** and require zero errors; record warnings separately and fix only those introduced by this plan.
- [ ] **Step 5: Run `git diff --check` and source scans** for forbidden sub-11px text, stale primary plan-card hierarchy, duplicate timers/samplers, and visible-launch commands added to scripts/tests.
- [ ] **Step 6: Verify repository hygiene**: no PowerFlow process was started by this plan, no untracked build artifacts outside ignored bin/obj, worktree state understood.
- [ ] **Step 7: STOP at the visual gate.** Report that tray-to-full-screen human visual validation remains blocked by the reference-host UI Safety rule until the user explicitly authorizes a specific live-UI pass. Do not infer permission from this plan or from `build it`.
