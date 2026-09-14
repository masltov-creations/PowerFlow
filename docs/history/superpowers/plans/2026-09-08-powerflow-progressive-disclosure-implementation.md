# PowerFlow Progressive Disclosure + Continuity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Give PowerFlow real pre-open continuity history and replace the dashboard card wall with one progressive-disclosure trajectory instrument shared with the tray popup.

**Architecture:** Add one application-level adaptive telemetry recorder that merges controller snapshots with a single rich telemetry source. Visual surfaces acquire disposable visibility leases instead of creating telemetry sessions. Project recorder history and policy state through pure trajectory/disclosure models into a compact dashboard and matching tray popup.

**Tech Stack:** .NET 8, WinUI 3 / Windows App SDK, xUnit, native Windows power/process integrations already present.

**Spec:** `docs/superpowers/specs/2026-09-08-powerflow-progressive-disclosure-design.md`

## Global Constraints

- Do not launch PowerFlow, preview windows, tray popups, UI Automation, cursor movement, or any visible UI on reference host during implementation/automated verification.
- Existing policy, manual latch, game latch, and plan activation semantics must remain unchanged.
- Hidden AUTO rich telemetry cadence is 5 seconds.
- Any visible PowerFlow visual surface raises rich telemetry cadence to 1 second.
- Hidden game/manual latch rich telemetry is off.
- Exactly one rich telemetry source exists per process.
- No telemetry disk persistence in this phase.
- No hidden render/animation timer.
- Typography floor and System/Light/Dark/Reduced Motion contracts remain intact.
- Preview/read-only mode must remain incapable of changing the real Windows power plan.

---

## Task 1: Extract adaptive telemetry cadence as pure policy

**Files:**
- Create: `src/PowerFlow.App/Telemetry/TelemetryCadencePolicy.cs`
- Create: `tests/PowerFlow.App.Tests/Telemetry/TelemetryCadencePolicyTests.cs`

- [x] Write tests for visible=1s, hidden AUTO=5s, hidden manual/game latch=Off, visible latch=1s.
- [x] Run targeted tests and confirm RED because policy does not exist.
- [x] Implement the minimal pure cadence policy and enum/value type.
- [x] Run targeted tests and confirm GREEN.
- [x] Commit `feat: add adaptive telemetry cadence policy`.

## Task 2: Build bounded application-level continuity recorder

**Files:**
- Create: `src/PowerFlow.App/Telemetry/ContinuitySample.cs`
- Create: `src/PowerFlow.App/Telemetry/TelemetryContinuityRecorder.cs`
- Create: `src/PowerFlow.App/Telemetry/TelemetryVisibilityLease.cs`
- Create: `tests/PowerFlow.App.Tests/Telemetry/TelemetryContinuityRecorderTests.cs`
- Modify minimally: `src/PowerFlow.App/Dashboard/DashboardTelemetrySession.cs` or extract a reusable rich-source runner only if required.

- [x] Write recorder tests for bounded ring, controller snapshot ingestion, rich sample merge, one source with two leases, cadence transitions, latch-off behavior, source failure isolation, null watt/GHz gaps.
- [x] Run targeted tests and confirm RED.
- [x] Implement recorder with injected source/tick/clock dependencies and one adaptive runner.
- [x] Ensure cadence changes restart only the recorder tick/source loop, never the policy controller.
- [x] Run targeted tests and confirm GREEN.
- [x] Commit `feat: add adaptive telemetry continuity recorder`.

## Task 3: Move telemetry ownership from windows to App

**Files:**
- Modify: `src/PowerFlow.App/App.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Tray/TrayHoverWindow.xaml.cs`
- Modify/Create tests under `tests/PowerFlow.App.Tests/Shell/` and `Dashboard/`.

- [x] Write source-contract/lifecycle tests proving App owns one recorder, MainWindow and TrayHoverWindow obtain/release visibility leases, and neither creates `DashboardTelemetrySession`.
- [x] Run targeted tests and confirm RED.
- [x] Instantiate recorder after controller creation in App and connect controller snapshots.
- [x] Pass recorder into MainWindow and TrayHoverWindow.
- [x] Replace local telemetry sessions with visibility leases and recorder subscriptions.
- [x] Ensure dashboard close releases lease and tray-only lifecycle remains alive.
- [x] Run targeted tests and confirm GREEN.
- [x] Commit `refactor: centralize PowerFlow telemetry continuity`.

## Task 4: Add pure trajectory and disclosure projections

**Files:**
- Create: `src/PowerFlow.App/Dashboard/TrajectoryProjection.cs`
- Create: `src/PowerFlow.App/Dashboard/DisclosureState.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/TrajectoryProjectionTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/DisclosureStateTests.cs`

- [x] Write tests for state intervals, real-transition markers only, NOW state/progress, quiet/promote rail metadata, honest gaps, conservative policy trajectory, and one expansion target at a time.
- [x] Run targeted tests and confirm RED.
- [x] Implement immutable projection records and pure projection logic.
- [x] Run targeted tests and confirm GREEN.
- [x] Commit `feat: add PowerFlow trajectory projection model`.

## Task 5: Rebuild default dashboard around one trajectory instrument

**Files:**
- Create: `src/PowerFlow.App/Dashboard/TrajectoryControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/TrajectoryControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs`
- Modify/add tests under `tests/PowerFlow.App.Tests/Dashboard/` and `Shell/`.

- [x] Write UI/source contract tests that reject permanent `RECENT`, `AUTOMATIC RULES`, and `DecisionPressure` default sections and require a dominant trajectory control.
- [x] Write projection/view-model tests proving pre-open recorder history is immediately exposed to the trajectory.
- [x] Run targeted tests and confirm RED.
- [x] Replace dashboard card-wall composition with top status strip + trajectory field + integrated mode nodes/threshold rails.
- [x] Preserve direct AUTO/SAVER/BALANCED/PERFORMANCE manual semantics using existing controller methods.
- [x] Render transition markers and state bands from trajectory projection.
- [x] Preserve graph hover exact values and threshold drag behavior through the new trajectory control.
- [x] Run targeted tests and confirm GREEN.
- [x] Commit `feat: make PowerFlow dashboard trajectory-first`.

## Task 6: Implement progressive disclosure hover lens and in-place expansion

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/TrajectoryControl.xaml(.cs)`
- Create as needed: `src/PowerFlow.App/Dashboard/TrajectoryLensProjection.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml(.cs)`
- Add tests under `tests/PowerFlow.App.Tests/Dashboard/`.

- [x] Write tests for hover lens payloads for sample/NOW/transition/rail/mode and disclosure selection/collapse behavior.
- [x] Run targeted tests and confirm RED.
- [x] Implement one local hover lens surface that follows the selected trajectory object.
- [x] Implement one primary in-place expansion target at a time: NOW, trajectory, quiet rail, promote rail, transition, mode node.
- [x] Ensure expansion never changes policy without explicit control interaction.
- [x] Respect Reduced Motion with crossfade/immediate geometry rather than travel animation.
- [x] Run targeted tests and confirm GREEN.
- [x] Commit `feat: add progressive PowerFlow disclosure interactions`.

## Task 7: Make tray popup the Level-0 form of the trajectory

**Files:**
- Modify: `src/PowerFlow.App/Tray/TrayHoverWindow.xaml`
- Modify: `src/PowerFlow.App/Tray/TrayHoverWindow.xaml.cs`
- Add tests under `tests/PowerFlow.App.Tests/Tray/` and `Shell/`.

- [x] Write tests requiring recorder-backed continuity, no popup-local telemetry session, state/lock header, NOW/recent transition projection, and semantic theme resources.
- [x] Run targeted tests and confirm RED.
- [x] Recompose popup to use recorder history and the same trajectory vocabulary/projection subset.
- [x] Keep popup non-activating and preserve hidden-overflow hover fix.
- [x] Ensure opening/closing popup only obtains/releases one visibility lease.
- [x] Run targeted tests and confirm GREEN.
- [x] Commit `feat: unify PowerFlow tray and dashboard trajectory`.

## Task 8: Headless regression, performance contracts, and hygiene

**Files:**
- Modify tests/docs only if defects are discovered.
- Do not launch PowerFlow UI on reference host.

- [x] Run all Core/Windows/App tests from scratch.
- [x] Run full solution build and require 0 warnings/0 errors.
- [x] Run `git diff --check` and staged diff hygiene.
- [x] Confirm no `PowerFlow.App` processes are running.
- [x] Source-scan that no visible-surface-local `DashboardTelemetrySession` remains.
- [x] Source-scan for hidden cadence constants and ensure 5s/1s/off rules are centralized.
- [x] Verify no new telemetry disk write path exists.
- [x] Update original plan/checklist with headless qualification evidence.
- [x] Commit final test/doc cleanup if needed.

## Headless qualification evidence — 2026-09-08

Implementation Tasks 1–8 completed without launching PowerFlow UI, preview, tray popup, cursor automation, or UI Automation on reference host.

Final clean-slate gate (`c6ea64e9-8cbf-43f5-9f1f-48b7aede6074`):

- `dotnet clean PowerFlow.sln`: succeeded, 0 warnings / 0 errors.
- Core tests from clean: 13 passed, 0 failed.
- Windows tests from clean: 28 passed, 0 failed.
- App tests from clean: 120 passed, 0 failed.
- Total tests: 161 passed, 0 failed.
- Full solution build after tests: succeeded, 0 warnings / 0 errors.
- `git diff --check` and `git diff --cached --check`: clean.
- Visible-surface/App references to `DashboardTelemetrySession`: 0.
- Production `new DashboardTelemetrySession` constructors: 0.
- Adaptive rich telemetry cadence policy contains the single 1s / 5s / Off definition; duplicate telemetry cadence constants: 0.
- Telemetry-layer disk write paths: 0.
- `PowerFlow.App` process count before and after headless gate: 0.

Human visual acceptance and subsequent real runtime/performance/game qualification remain intentionally unexecuted because UI interaction on reference host was explicitly prohibited during implementation.
## Human acceptance gate (not executed automatically)

Only after the headless gate is green and the user explicitly permits UI interaction on reference host:

- open the real app once;
- verify pre-open continuity is visible immediately;
- inspect compact trajectory/default disclosure visually;
- test hover lenses and click expansion;
- test tray mini-dashboard;
- then run performance/game qualification separately.
