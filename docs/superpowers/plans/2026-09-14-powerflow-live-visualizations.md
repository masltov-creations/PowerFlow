# PowerFlow Live Visualizations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the six visualization directions as honest, bounded presentations over retained PowerFlow data, with Signal Desk as the default Live view.

**Architecture:** Introduce one immutable presentation-snapshot/quality/selection layer over existing retained telemetry, then implement each view as a pure projection and bounded renderer inside the persistent shell. Existing recorder, controller, profile authority, shadow runtime, acquisition cadence, retention, and shell motion remain authoritative.

**Tech Stack:** .NET 8, WinUI 3 / Windows App SDK, existing PowerFlow telemetry/controller runtimes, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-14-powerflow-six-visualization-directions.md`

## Global Constraints

- The one-click installer/public-release qualification is Task 0 and blocks visualization implementation.
- No visualization may add telemetry polling, sensor handles, governor evaluation, policy writes, or a second shell-size state machine.
- Keep Model Zone, confirmed Applied Profile, and Authority independent.
- Preserve Unknown/Stale/Unsupported/Missing/Invalid/zero distinctions and source units/scales.
- `TelemetryContinuityRecorder` owns retention; presentation reads bounded visible intervals only.
- Exact decision pairing requires aligned observation sequence and known configuration revision.
- Shared timestamp/core selection must be consistent across views.
- Respect text scaling, light/dark/high-contrast, reduced motion, and non-color state cues.
- Meet the spec's CPU/UI/GPU/memory budgets before enabling expensive optional motion.
- Native desktop acceptance requires explicit operator authorization after headless checks are green.

### Task 0: Finish installer and public release

**Files:** `tools/install/*`, `tools/PowerFlow.Setup/*`, `README.md`, `tests/PowerFlow.App.Tests/Startup/InstallLayoutContractTests.cs`.

- [x] Verify the WinUI PRI/XBF regression contract and corrected payload.
- [x] Build Setup and run embedded-payload verification.
- [x] Run migration install, shortcut launch, running-copy upgrade, uninstall preserving user data, and reinstall acceptance.
- [x] Run full tests, Release build, whitespace/privacy gates.
- [x] Publish `PowerFlow-Setup.exe` and `.sha256` as a GitHub Release and verify remote hash/README.
- [x] Commit/push release evidence before Task 1.

**Task 0 release evidence (2026-09-14):**
- Release tag: `v0.1.0-beta.5` at `da06a38633ad10875df4a588ea8e939e4c19953a`.
- Public release: `https://github.com/masltov-creations/PowerFlow/releases/tag/v0.1.0-beta.5`.
- Frozen installer SHA-256: `0297CCE424C2B72AB3D27D2CD199CFA504DB85A7892592FF1B04E0BC0B092923`.
- Qualification: 626/626 tests passed on the tag tree; Release build was clean; install/update/uninstall/reinstall acceptance passed; payload contains no PDBs or first-party local build paths.
- Public verification: downloaded GitHub EXE hash and published `.sha256` both matched the frozen installer hash.
### Task 1: Shared presentation contracts

**Create:** `src/PowerFlow.App/Visualization/PresentationSnapshot.cs`, `SignalQuality.cs`, `VisualizationSelection.cs`, `PresentationSnapshotProjector.cs`.
**Test:** `tests/PowerFlow.App.Tests/Visualization/PresentationSnapshotProjectorTests.cs`.

- [ ] RED fixtures: aligned sequences, clock jumps, stale/missing/unsupported signals, topology Unknown, independent Model/Applied timestamps, active/shadow revisions.
- [ ] Implement immutable source/value/quality/snapshot types with no UI dependencies.
- [ ] Implement bounded projector and shared timestamp/core selection.
- [ ] Run focused tests plus timeline/controller regressions.
- [ ] Commit shared presentation layer.

### Task 2: Signal Desk default Live view

**Modify:** `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml*`.
**Create:** `src/PowerFlow.App/Visualization/SignalDeskProjection.cs`.

- [ ] RED tests: shared cursor, missing-watts gaps, pressure source/domain, core-state conservation, Model/Applied separation, bounded decimation.
- [ ] Implement pure projection and geometry caching by history revision/range/viewport/quality.
- [ ] Integrate four lanes plus separate Model/Applied steps and recorded decision explanation without new acquisition/retention.
- [ ] Add Compact/Expanded density, keyboard/touch/pointer selection, reduced motion/high contrast.
- [ ] Run regression/resource tests; make Signal Desk default; commit.

### Task 3: Core Atlas

**Create:** `CoreAtlasProjection.cs`, `CoreAtlasControl.xaml*`.

- [ ] RED fixtures for 4/8/16/24/64 supported cores, SMT on/off, missing members, Unknown, unsupported multi-group coverage.
- [ ] Implement stable schematic topology adapter without utilization sorting or fake die geometry.
- [ ] Implement bounded tiles, accessible pointer/keyboard selection, aggregate fallback.
- [ ] Verify counts+Unknown equal topology size and no inferred zero clock; commit.

### Task 4: Parallel Worlds

**Create:** `ParallelWorldsProjection.cs`, `ParallelWorldsControl.xaml*`.

- [ ] RED tests for exact sequence/revision alignment, missing shadow gaps, manual ULTRA/game authority, failed/delayed actuation, zero shadow-only writes.
- [ ] Implement bounded join/projection; exclude unaligned data from statistics.
- [ ] Render actual/shadow ribbons, disagreement intervals, authority annotation, shared scrub/detail.
- [ ] Assert no counterfactual watts/savings/temperature/latency/speedup claims; commit.

### Task 5: Operating Orbit

**Create:** `OperatingOrbitProjection.cs`, `OperatingOrbitControl.xaml*`.

- [ ] RED tests for missing/stale watts gaps, pressure fallback, loops, endpoints/spikes, chronological overlap selection.
- [ ] Implement bounded decimation preserving turns/gaps/endpoints/source sample indices.
- [ ] Render measured pressure-vs-watts path with stable domains and Signal Desk fallback.
- [ ] Run resize/text-scale/replay regressions; commit.

### Task 6: Tension Lens semantic adapter

**Create:** `TensionLensAdapter.cs`, `TensionLensControl.xaml*`.

- [ ] Audit actual governor configuration members/units before enabling axes.
- [ ] RED golden fixtures for every enabled mapping/direction and omitted-axis reasons.
- [ ] Implement only contractually supported mappings; no arbitrary profile-rank/EPP/core-floor inference.
- [ ] Render Compact interval bars / Expanded lens, falling back below three comparable dimensions.
- [ ] Prove tension edits are shadow-only; commit after qualification.

### Task 7: Silicon Weather opt-in

**Create:** `SiliconWeatherProjection.cs`, `SiliconWeatherControl.xaml*`; add a feature flag.

- [ ] RED tests for relief/top-down equivalence, visible Unknown, bounded geometry, exact selection.
- [ ] Implement cached batched geometry with <=160 time columns and <=64 count bands in the existing rendering layer.
- [ ] Add Compact ribbon and static Expanded/Workspace relief behind opt-in flag.
- [ ] Validate reduced motion, high contrast, 200% text scaling, accessibility.
- [ ] Measure matched-input CPU/UI/GPU/memory; reduce geometry/motion first if missed.
- [ ] Enable opt-in only if static gates pass; commit qualification state.

### Task 8: Unified selector and release qualification

**Modify:** `MainWindow.xaml*` and existing user config surface found during Task 2.

- [ ] RED tests proving visualization choice is local UI state and causes zero policy writes/new acquisition.
- [ ] Add Live selector with Signal Desk default and shared timestamp/core selection.
- [ ] Run full required fixture matrix across enabled views.
- [ ] Run all tests/build plus matched CPU/GPU/memory measurements and record method/noise/results.
- [ ] With explicit operator authorization, run native resize/selection/reduced-motion/high-contrast acceptance.
- [ ] Run privacy gate; capture screenshots only from verified native PowerFlow process/HWND state.
- [ ] Commit acceptance evidence and publish qualified release.