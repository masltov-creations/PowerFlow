# PowerFlow First-User Interaction Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make PowerFlow's existing adaptive-governor backend usable as a coherent first-user product: stable resizing, smooth truthful graphs, policy overlaid directly on live data in X and Y, obvious mode control, direct graph tuning, explainable Model, and complete hover/selection behavior.

**Architecture:** Preserve the qualified telemetry, calibration, entitlement, governor, persistence, and actuator backend. Replace the interaction layer with a single persistent analytical surface whose geometry, policy overlays, hover, selection, and tuning layers have independent lifetimes. Separate shell presentation state from responsive density so physical window resizing cannot feed back into semantic state.

**Tech Stack:** .NET 8, C# 12, WinUI 3, Windows App SDK, PDH/Windows performance telemetry, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-09-powerflow-adaptive-performance-envelope-design.md` sections 7-15 and corrective section 20.

## Global Constraints

- reference host live UI remains blocked unless the user explicitly authorizes that specific acceptance pass.
- No Qwen or alternate coding lane unless explicitly approved by the user.
- Do not change the established adaptive governor, calibration, entitlement, persistence, or actuator semantics except where required to expose the approved interaction contract.
- Every production behavior change uses RED -> GREEN TDD.
- Every visible metric must have an explicit unavailable state; never render an unexplained empty lane or selectable empty Atlas dimension.
- Graph smoothing must be shape-preserving and must not overshoot measured local extrema.
- All normal-window layout calculations use logical DIPs. Convert physical `AppWindow` pixels exactly once at the window boundary.
- No second telemetry sampler or timer.
- No merge, push, tag, publish, or release without explicit user authorization.

---

### Task 1: Separate shell state from responsive density

**Files:**
- Create: `src/PowerFlow.App/Dashboard/ShellResponsiveDensity.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PowerFlowShellLayout.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellResponsiveDensityTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/PowerFlowShellLayoutTests.cs`

**Interfaces:**
- Produces: `ShellResponsiveDensity.Resolve(ShellLogicalSize size, ShellDensity previous) -> ShellDensity` with `Compact` and `Expanded` densities and hysteresis.
- `PowerFlowShellState` remains explicit presentation intent; `ShellDensity` controls normal-window disclosure only.

- [ ] **Step 1: Write failing density/state tests.** Add tests asserting `950x550 physical @1.25 -> 760x440 logical -> Compact`, Expanded enters at logical `>=900x560`, Expanded remains Expanded inside the hysteresis band, Compact remains Compact until the enter threshold is crossed, and `OnAppWindowChanged` no longer assigns `_shellState` from width/height.
- [ ] **Step 2: Run** `dotnet test tests/PowerFlow.App.Tests/PowerFlow.App.Tests.csproj -c Release --filter FullyQualifiedName~ShellResponsiveDensity --no-restore` and verify RED because `ShellResponsiveDensity` does not exist.
- [ ] **Step 3: Implement `ShellResponsiveDensity`.** Use enter thresholds `900x560`, exit thresholds `860x520`; Glance/FullScreen are not resolved by this type.
- [ ] **Step 4: Update `MainWindow`.** Add `_layoutDensity`; convert `AppWindow.Size` with `CurrentLogicalAppWindowSize()` once; `OnAppWindowChanged` changes `_layoutDensity` and calls layout, but never changes `_shellState`; explicit Compact/Expanded transitions seed density; PowerFlow-owned animation cannot trigger density/state feedback.
- [ ] **Step 5: Update `PowerFlowShellLayout`** to resolve normal-window semantic presentation from `ShellDensity`, not inferred `PowerFlowShellState` mutation.
- [ ] **Step 6: Run focused shell/morph tests and full App tests; require GREEN.**
- [ ] **Step 7: Commit** `fix: stabilize PowerFlow responsive density`.

### Task 2: Replace Polyline Timeline with persistent shape-preserving geometry

**Files:**
- Create: `src/PowerFlow.App/Dashboard/ShapePreservingCurve.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineProjection.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShapePreservingCurveTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/PerformanceTimelineContractTests.cs`

**Interfaces:**
- Produces: `ShapePreservingCurve.Build(IReadOnlyList<Point> points) -> PathGeometry` or an equivalent pure segment model consumed by the WinUI renderer.
- Timeline has four persistent trace `Path` elements, independent persistent policy, selection, hover, and label layers.

- [ ] **Step 1: Write failing curve tests.** Assert each segment passes through measured endpoints, control values stay within neighboring sample extrema for monotone input, repeated/flat values remain flat, missing-value runs become separate figures, and no NaN/Infinity geometry is produced.
- [ ] **Step 2: Write failing source-contract tests.** Require no primary `Polyline`, no `PlotCanvas.Children.Clear()` during ordinary data refresh, four named persistent trace paths, and per-lane clipping.
- [ ] **Step 3: Run focused tests and verify RED.**
- [ ] **Step 4: Implement shape-preserving monotone cubic interpolation** using Fritsch-Carlson-style tangents and cubic Bezier control points; split figures at missing samples.
- [ ] **Step 5: Convert Timeline rendering to persistent paths.** Create/retain one path per KPI lane; update only `Path.Data`, transforms, and clip rectangles on data/size changes. Do not rebuild unrelated policy/selection/hover layers.
- [ ] **Step 6: Run focused tests, full App tests, and Release build; require GREEN with zero errors/warnings introduced.**
- [ ] **Step 7: Commit** `feat: render smooth truthful PowerFlow timeline`.

### Task 3: Overlay policy on live data and make Tune direct manipulation

**Files:**
- Create: `src/PowerFlow.App/Dashboard/TimelinePolicyOverlayProjection.cs`
- Create: `src/PowerFlow.App/Dashboard/TimelinePolicyInteraction.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/EnvelopeTuningViewModel.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/TimelinePolicyOverlayTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/AdaptiveTuningContractTests.cs`

**Interfaces:**
- Produces: horizontal value rails for CPU thresholds/power frontier; vertical time bands/edges for qualification, lease, and release durations; `PolicyHandleChanged` event carrying semantic candidate tuning.
- `SetTuneMode(bool)` enables hit testing/handles without replacing the Timeline.

- [ ] **Step 1: Write failing projection tests** proving Y rails map to the correct KPI lane/native scale and X duration bands map to the shared time scale, with learned and candidate positions retained separately.
- [ ] **Step 2: Write failing interaction tests** proving dragging a Y handle changes only the candidate threshold, dragging an X edge changes only its duration, and unsaved edits do not mutate persisted config.
- [ ] **Step 3: Run focused tests and verify RED.**
- [ ] **Step 4: Implement persistent policy overlay geometry** in independent `PolicyValueLayer`, `PolicyTimeLayer`, and `PolicyHandleLayer` elements; never clear trace geometry to update policy.
- [ ] **Step 5: Implement direct pointer manipulation** with bounded hit targets, pointer capture, candidate-only updates, learned ghost rails, and mirrored numeric values.
- [ ] **Step 6: Recompose Tune** so the Timeline remains dominant; move numeric controls into the contextual precision inspector and remove detached duplicate primary sliders where the graph now supplies the direct control.
- [ ] **Step 7: Run focused tuning/Timeline tests and full App tests; require GREEN.**
- [ ] **Step 8: Commit** `feat: tune PowerFlow policy directly on timeline`.

### Task 4: Restore obvious primary mode authority

**Files:**
- Create: `src/PowerFlow.App/Dashboard/PowerModeStripControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/PowerModeStripControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/PowerModeStripContractTests.cs`
- Test: `tests/PowerFlow.App.Tests/Controller/PowerFlowControllerTests.cs`

**Interfaces:**
- Produces: `ModeRequested` with `Auto` or `EnvelopeZone` from an always-visible `AUTO | ECO | EFFICIENT | RESPONSIVE | BOOST` strip from Compact upward.
- MainWindow maps explicit modes through the existing manual controller path; Auto calls `ReleaseManualLatchAsync`.

- [ ] **Step 1: Write failing source/behavior tests** requiring the five visible modes, selected-state feedback, Auto release, and explicit modes to call the manual controller path without requiring adaptive actuation to be enabled.
- [ ] **Step 2: Run focused tests and verify RED.**
- [ ] **Step 3: Implement `PowerModeStripControl`** with compact hit targets, keyboard accessibility, selected/manual feedback, and short tooltips explaining each mode in user language.
- [ ] **Step 4: Wire `MainWindow`** so Eco maps to Power Saver, Efficient to Balanced, Responsive to the responsive manual policy using the current actuator capability (Balanced actuator until a distinct actuator exists), Boost to High Performance, and Auto releases the latch. The header must show `MANUAL` when an explicit mode owns authority.
- [ ] **Step 5: Run focused controller/header tests and full App tests; require GREEN.**
- [ ] **Step 6: Commit** `feat: restore direct PowerFlow mode control`.

### Task 5: Make Model explain the machine and make Atlas usable

**Files:**
- Create: `src/PowerFlow.App/Dashboard/ModelExplanationProjection.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceAtlasControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceAtlasControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceAtlasProjection.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ModelExplanationProjectionTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/PerformanceAtlasContractTests.cs`

**Interfaces:**
- Produces: `ModelExplanation` with `WhatLearned`, `WhatDoingNow`, `ConfidenceExplanation`, and current-point coordinates when available.
- Atlas disables dimensions with zero usable observations and exposes a `YOU ARE HERE` marker.

- [ ] **Step 1: Write failing explanation tests** for high/medium/low confidence, current efficient/expensive region, manual mode, braking/qualifying/lease decisions, and unavailable evidence.
- [ ] **Step 2: Write failing Atlas contracts** requiring three plain-language explanation fields, current-point marker, dimension availability checks, and an explicit unavailable message instead of an empty plot.
- [ ] **Step 3: Run focused tests and verify RED.**
- [ ] **Step 4: Implement `ModelExplanationProjection`** using only learned envelope, confidence, latest truthful observation, entitlement, and governor decision.
- [ ] **Step 5: Update Atlas UI** with the explanation block and `YOU ARE HERE`; disable unavailable dimensions and automatically choose a valid pair without silently changing meaning.
- [ ] **Step 6: Run focused tests and full App tests; require GREEN.**
- [ ] **Step 7: Commit** `feat: explain PowerFlow machine model`.

### Task 6: Add unified hover/selection feedback without redraw artifacts

**Files:**
- Create: `src/PowerFlow.App/Dashboard/InspectionExplanationProjection.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceAtlasControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceAtlasControl.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/InspectionExplanationProjectionTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/AdaptiveTuningContractTests.cs`

**Interfaces:**
- Produces one plain-language inspection model shared by Timeline/Atlas hover cards.
- Hover is transient; selection remains persistent and exact-index linked between projections.

- [ ] **Step 1: Write failing formatter tests** requiring time, CPU, watts, GHz, cores awake, actor when known, mode/policy, and plain-language decision; unavailable fields must say unavailable rather than `-` where ambiguity matters.
- [ ] **Step 2: Write failing hover contracts** requiring independent hover layers, policy-handle tooltips, Atlas cell hover/highlight, and no mutation of persistent selection on hover.
- [ ] **Step 3: Run focused tests and verify RED.**
- [ ] **Step 4: Implement unified inspection model and Timeline hover lens.** Highlight the nearest synchronized timestamp across all KPI lanes and the relevant policy rails.
- [ ] **Step 5: Implement Atlas hover.** Highlight the cell, linked Timeline observation indices, current/learned relation, residency, and associated actor/decision evidence without committing selection until click.
- [ ] **Step 6: Add restrained opacity/glow transitions** to hover/selection elements only; respect Reduced Motion.
- [ ] **Step 7: Run focused tests, full App tests, and Release build; require GREEN.**
- [ ] **Step 8: Commit** `feat: add PowerFlow inspection lens`.

### Task 7: First-user end-to-end qualification gate

**Files:**
- Create: `tests/PowerFlow.App.Tests/Dashboard/FirstUserInteractionContractTests.cs`
- Modify only if tests identify a real defect in the implemented tasks above.

**Interfaces:**
- Qualifies the entire visible chain before live acceptance: `sensor -> retained observation -> projection -> renderer -> hover/selection -> policy interaction`.

- [ ] **Step 1: Add source/behavior contracts** proving supported KPI lanes have data/unavailable explanations, Atlas cannot offer unusable dimensions, mode strip owns manual/Auto actions, Tune controls are graph-linked, and persistent render layers do not clear/recreate unrelated geometry.
- [ ] **Step 2: Run full Core tests** in Release.
- [ ] **Step 3: Run full Windows tests** in Release.
- [ ] **Step 4: Run full App tests** in Release.
- [ ] **Step 5: Run exact Release solution build** and require zero errors and no newly introduced warnings.
- [ ] **Step 6: Run `git diff --check`, duplicate timer/sampler scan, and confirm no `PowerFlow.App` process is running.**
- [ ] **Step 7: Run headless real-reference host telemetry probe** and require non-null CPU pressure, package watts, clock, and cores-awake where those sensors are supported.
- [ ] **Step 8: STOP before live UI.** Ask for a fresh explicit live acceptance authorization. The live pass must repeatedly exercise Glance, Compact, arbitrary manual resize, Expanded, Model, Tune, Full Screen, Restore, hover, direct policy drag, mode changes, and verify no visual artifacts/clipping/state drift.
