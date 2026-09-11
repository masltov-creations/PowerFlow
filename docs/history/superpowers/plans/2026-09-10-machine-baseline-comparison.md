# Machine Baseline Comparison Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a one-click, reversible six-mode five-minute-per-mode CPU/power baseline with overlaid comparison curves and evidence-based recommendations.

**Architecture:** Add durable comparison models/analysis in PowerFlow.Core, make the existing CPU profiler duration-configurable, add an app-owned orchestration runtime that owns mode sequencing/idle telemetry/restoration, then surface the results on the existing Profile page. Persist only completed runs through the existing PowerFlow config path.

**Tech Stack:** .NET 8, WinUI 3, Windows power-plan/processor-policy APIs, existing DashboardTelemetrySource, existing CpuCapabilityProfiler.

**Spec:** `docs/superpowers/specs/2026-09-10-machine-baseline-comparison-design.md`

## Global Constraints
- Standard sequence is Windows Saver, Windows Balanced, BAL-E, BAL-P, Performance, Auto.
- Default duration is exactly 5 minutes per mode: 15 s settle + 90 s idle + 39 s x 5 benchmark points.
- Every mode transition and final exit is reversible; cancellation/fault restores the pre-run state.
- Native Windows baseline legs have no PowerFlow processor-policy overlay.
- Auto is last and uses the current governor implementation.
- Recommendation math is transparent; no opaque composite score.
- Do not start a standard 30-minute baseline automatically; the user starts it with the button.

---

### Task 1: Comparison model and recommendation analysis
**Files:** create `src/PowerFlow.Core/Profiling/MachineBaselineComparison.cs`; test `tests/PowerFlow.Core.Tests/Profiling/MachineBaselineComparisonTests.cs`.
- [ ] Write failing tests for sequence, five-minute schedule, recommendation, and idle deltas.
- [ ] Run tests and verify RED.
- [ ] Implement the minimal comparison model/analysis.
- [ ] Run tests GREEN.

### Task 2: Config persistence
**Files:** modify `src/PowerFlow.Core/Rules/PowerFlowConfig.cs`; add config compatibility tests.
- [ ] Write failing compatibility/bounded-history tests.
- [ ] Verify RED.
- [ ] Add optional baseline-run history with empty fallback.
- [ ] Verify GREEN.

### Task 3: Duration-configurable CPU profiler
**Files:** modify `src/PowerFlow.App/Controller/CpuCapabilityProfiler.cs`; tests under `tests/PowerFlow.App.Tests/Controller`.
- [ ] Write failing tests for default/current-profile options and five-minute baseline point duration.
- [ ] Verify RED.
- [ ] Add options without changing existing PROFILE CURRENT behavior.
- [ ] Verify GREEN.

### Task 4: Reversible six-mode runner
**Files:** create `src/PowerFlow.App/Controller/MachineBaselineRunner.cs` and mode-host support; tests under `tests/PowerFlow.App.Tests/Controller`.
- [ ] Write failing tests for exact sequence, restoration on success/cancel/fault, and native-vs-PowerFlow mode entry.
- [ ] Verify RED.
- [ ] Implement mode host, idle capture, profiler orchestration, progress, and final restoration.
- [ ] Verify GREEN.

### Task 5: Comparison UI
**Files:** modify `CpuCapabilityProfileControl.xaml/.cs`; create focused chart control if needed; update `MainWindow.xaml.cs`; tests under Dashboard contracts.
- [ ] Write failing UI/contract tests for BASELINE MACHINE, metric selector, overlay chart, idle comparison, knee/result table, recommendation and progress/cancel.
- [ ] Verify RED.
- [ ] Implement the UI and persistence callback.
- [ ] Verify GREEN.

### Task 6: Full qualification
- [ ] Run focused tests.
- [ ] Run full PowerFlow solution tests.
- [ ] Build Release with zero warnings/errors.
- [ ] Run corruption/diff hygiene scans.
- [ ] Deploy exact verified build and open Profile page for user review without starting the 30-minute run automatically.
- [ ] Commit/push and verify local/remote hash equality.