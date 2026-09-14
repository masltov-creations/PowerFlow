# PowerFlow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a tray-first native Windows utility that rests in Power Saver, promotes to Balanced on sustained CPU demand, latches High Performance for games/manual overrides, and exposes a modern motion-flow dashboard while remaining measurably negligible in the background.

**Architecture:** A single .NET 8 WinUI 3 process hosts a pure policy state machine, thin Win32 power-plan integration, a low-rate activity monitor, event-driven game/process lifecycle tracking, an atomic JSON rule store, a Win32 tray icon, and an on-demand WinUI dashboard. Hidden/dashboard-closed operation is deliberately timer-light; Game Latch suspends normal telemetry and demotion logic and waits on process lifecycle events instead.

**Tech Stack:** C# / .NET SDK 8.0.424; `net8.0-windows10.0.19041.0`; Windows App SDK 2.4.0 / WinUI 3; xUnit; Win32 P/Invoke (`powrprof.dll`, `kernel32.dll`, `shell32.dll`, `user32.dll`); `System.Management` only for bounded process start/stop event subscriptions outside Game Latch, subject to the overhead gate.

**Spec:** `docs/superpowers/specs/2026-09-08-powerflow-design.md`

## Global Constraints

- Power Saver is the normal resting state, but a configuration flag must allow Balanced to be used as the resting state while reference host's Power Saver hard-freeze investigation remains unresolved.
- CPU spikes alone do not promote; CPU demand must persist for a configurable 3-5 second window.
- Balanced demotion requires a configurable 20-30 second quiet hysteresis window.
- A recognized game creates a hard High Performance latch that cannot be released by low CPU/GPU utilization.
- Manual High Performance is latched until explicitly released.
- Game-latched/dashboard-closed mode performs no normal CPU/GPU telemetry polling and no idle-based demotion evaluation.
- No 100-250 ms background timers; normal closed-dashboard sampling begins at 2 seconds and is coalesced.
- The app switches active plans only; it never rewrites hidden processor settings or power-plan contents during normal operation.
- Every automatic transition carries an explainable reason.
- No Electron/browser runtime, no database, no privileged service unless later evidence proves one is necessary.
- No continuous WMI enumeration. WMI process events are permitted only as subscriptions and must be disabled during Game Latch if process handles are sufficient.
- Dashboard visuals are created/rendered only while open and must support Windows Reduced Motion.
- Configuration writes are atomic and recover from a last-known-good copy.
- Startup is per-user, minimized to tray, with no console windows or startup popups.
- Acceptance requires PowerFlow-on versus controller-off measurements for package power, total/privileged CPU, timer/wakeup activity where available, disk writes, and game frame-time impact.
- Do not declare production-ready until the underlying reference host Power Saver stability question is resolved.

## File Structure

```text
PowerFlow.sln
Directory.Packages.props
src/
  PowerFlow.Core/
    PowerFlow.Core.csproj
    Policy/PowerState.cs
    Policy/PolicyEvent.cs
    Policy/PolicyDecision.cs
    Policy/PolicyConfig.cs
    Policy/PowerPolicyEngine.cs
    Rules/AppRule.cs
    Rules/PowerFlowConfig.cs
  PowerFlow.Windows/
    PowerFlow.Windows.csproj
    Power/PowerPlanIds.cs
    Power/IPowerPlanController.cs
    Power/WindowsPowerPlanController.cs
    Activity/IActivitySource.cs
    Activity/SystemTimesActivitySource.cs
    Activity/DashboardTelemetrySource.cs
    Games/GameProcess.cs
    Games/IGameLifecycleMonitor.cs
    Games/GameLifecycleMonitor.cs
    Games/GameHeuristic.cs
    Foreground/ForegroundWindowProbe.cs
    Configuration/JsonConfigStore.cs
    Startup/StartupRegistration.cs
  PowerFlow.App/
    PowerFlow.App.csproj
    App.xaml
    App.xaml.cs
    Controller/PowerFlowController.cs
    Controller/ControllerSnapshot.cs
    Tray/TrayIconHost.cs
    Tray/TrayMenuCommands.cs
    Dashboard/MainWindow.xaml
    Dashboard/MainWindow.xaml.cs
    Dashboard/FlowFieldControl.xaml
    Dashboard/FlowFieldControl.xaml.cs
    Dashboard/DashboardViewModel.cs
    Dashboard/Converters.cs
    Settings/RulesPage.xaml
    Settings/RulesPage.xaml.cs
    Settings/SettingsPage.xaml
    Settings/SettingsPage.xaml.cs
    Assets/
    Package.appxmanifest
  PowerFlow.PerfHarness/
    PowerFlow.PerfHarness.csproj
    Program.cs
tests/
  PowerFlow.Core.Tests/
    Policy/PowerPolicyEngineTests.cs
    Rules/ConfigTests.cs
  PowerFlow.Windows.Tests/
    Power/WindowsPowerPlanControllerTests.cs
    Games/GameLifecycleMonitorTests.cs
    Configuration/JsonConfigStoreTests.cs
  PowerFlow.App.Tests/
    Controller/PowerFlowControllerTests.cs
scripts/
  Measure-PowerFlowOverhead.ps1
  Verify-PowerFlowHygiene.ps1
```

---

### Task 1: Solution skeleton and pure policy state machine

**Files:**
- Create: `PowerFlow.sln`, `Directory.Packages.props`
- Create: `src/PowerFlow.Core/PowerFlow.Core.csproj`
- Create: `src/PowerFlow.Core/Policy/PowerState.cs`
- Create: `src/PowerFlow.Core/Policy/PolicyEvent.cs`
- Create: `src/PowerFlow.Core/Policy/PolicyDecision.cs`
- Create: `src/PowerFlow.Core/Policy/PolicyConfig.cs`
- Create: `src/PowerFlow.Core/Policy/PowerPolicyEngine.cs`
- Create: `tests/PowerFlow.Core.Tests/PowerFlow.Core.Tests.csproj`
- Create: `tests/PowerFlow.Core.Tests/Policy/PowerPolicyEngineTests.cs`

**Interfaces:**
- Produces: `PowerState`, `PolicyEvent`, `PolicyDecision`, `PolicyConfig`, and `PowerPolicyEngine.Evaluate(PolicyEvent evt) -> PolicyDecision`.
- `PolicyDecision` must include `PowerState Target`, `bool Changed`, `string Reason`, `bool IsLatched`, and `DateTimeOffset DecidedAt`.

- [ ] **Step 1: Scaffold the solution and test project with pinned package versions**

Create `Directory.Packages.props` with central versions for `Microsoft.WindowsAppSDK` 2.4.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, and `Microsoft.NET.Test.Sdk` 17.14.1. Create a `net8.0` Core library and xUnit test project, then add them to `PowerFlow.sln`.

Run: `dotnet restore PowerFlow.sln`
Expected: restore succeeds with Windows App SDK not yet referenced by Core.

- [ ] **Step 2: Write failing policy tests**

Tests must cover these exact scenarios using deterministic synthetic timestamps:

```csharp
[Fact] public void ShortCpuSpike_DoesNotLeaveRestingState();
[Fact] public void SustainedCpuDemand_PromotesToBalancedAfterThresholdWindow();
[Fact] public void BalancedQuietPeriod_DemotesOnlyAfterHysteresis();
[Fact] public void GameStart_JumpsDirectlyToPerformanceAndLatches();
[Fact] public void GameTelemetryDroppingToZero_DoesNotReleaseLatch();
[Fact] public void GameExit_EntersCooldownThenBalanced();
[Fact] public void ManualPerformance_RemainsLatchedUntilExplicitRelease();
[Fact] public void ManualLatch_OutranksGameAndCpuRules();
[Fact] public void ExplicitBalancedRule_OutranksCpuQuietDemotion();
[Fact] public void BalancedRestingStateFlag_PreventsPowerSaverDemotion();
```

Run: `dotnet test tests/PowerFlow.Core.Tests/PowerFlow.Core.Tests.csproj`
Expected: FAIL because policy types are not implemented.

- [ ] **Step 3: Implement the minimal deterministic state machine**

Use monotonic event timestamps supplied by tests; do not read wall-clock time inside the engine. Define events for `CpuSample`, `GameStarted`, `GameExited`, `ManualPerformanceRequested`, `ManualPerformanceReleased`, `ExplicitBalancedActivated`, `ExplicitBalancedCleared`, and `CooldownExpired`. Track only state required to make the next decision.

- [ ] **Step 4: Run the policy suite**

Run: `dotnet test tests/PowerFlow.Core.Tests/PowerFlow.Core.Tests.csproj`
Expected: all policy tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add PowerFlow.sln Directory.Packages.props src/PowerFlow.Core tests/PowerFlow.Core.Tests
git commit -m "feat: add deterministic power policy engine"
```

---

### Task 2: Versioned configuration, rules, and atomic recovery

**Files:**
- Create: `src/PowerFlow.Core/Rules/AppRule.cs`
- Create: `src/PowerFlow.Core/Rules/PowerFlowConfig.cs`
- Create: `src/PowerFlow.Windows/PowerFlow.Windows.csproj`
- Create: `src/PowerFlow.Windows/Configuration/JsonConfigStore.cs`
- Create: `tests/PowerFlow.Core.Tests/Rules/ConfigTests.cs`
- Create: `tests/PowerFlow.Windows.Tests/PowerFlow.Windows.Tests.csproj`
- Create: `tests/PowerFlow.Windows.Tests/Configuration/JsonConfigStoreTests.cs`

**Interfaces:**
- Produces: `PowerFlowConfig` schema version 1 with plan GUIDs, `RestingState`, CPU promotion threshold/window, quiet demotion threshold/window, cooldown duration, app rules, startup preference, reduced-motion preference.
- Produces: `JsonConfigStore.LoadAsync() -> Task<PowerFlowConfig>` and `SaveAsync(PowerFlowConfig config) -> Task`.

- [ ] **Step 1: Write schema/default tests**

Assert defaults: rest state `PowerSaver`, CPU promotion threshold `35.0`, promotion window `4s`, quiet threshold `12.0`, quiet window `25s`, post-game cooldown `8s`; rules default empty; plan GUID fields nullable until discovery.

- [ ] **Step 2: Write atomic-write/recovery tests**

Use a temporary directory. Verify save writes `config.json.tmp`, atomically replaces `config.json`, maintains `config.json.lastgood`, and `LoadAsync` falls back to last-good if primary JSON is malformed.

Run: `dotnet test tests/PowerFlow.Core.Tests tests/PowerFlow.Windows.Tests`
Expected: FAIL before implementation.

- [ ] **Step 3: Implement immutable config records and atomic store**

Use `System.Text.Json` with camelCase, indented human-readable output, schema-version validation, and `File.Replace`/same-volume rename semantics. Never silently discard an invalid primary without recording a diagnostic result.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test PowerFlow.sln`
Expected: PASS.

```powershell
git add src/PowerFlow.Core/Rules src/PowerFlow.Windows/Configuration tests
git commit -m "feat: add atomic PowerFlow configuration store"
```

---

### Task 3: Windows power-plan discovery and verified switching

**Files:**
- Create: `src/PowerFlow.Windows/Power/PowerPlanIds.cs`
- Create: `src/PowerFlow.Windows/Power/IPowerPlanController.cs`
- Create: `src/PowerFlow.Windows/Power/WindowsPowerPlanController.cs`
- Create: `tests/PowerFlow.Windows.Tests/Power/WindowsPowerPlanControllerTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<PowerPlanInfo>> ListAsync()`; `Task<PowerPlanInfo> GetActiveAsync()`; `Task<PowerPlanSwitchResult> ActivateAsync(Guid schemeId)`.
- `ActivateAsync` must verify the active GUID after `PowerSetActiveScheme`; a Win32 success code without matching read-back is a failure.

- [ ] **Step 1: Write native-boundary tests using an injectable adapter**

Tests cover Power Saver/Balanced/High Performance GUID discovery, absent High Performance plan, activation success, activation Win32 error, and mismatched read-back.

- [ ] **Step 2: Implement `powrprof.dll` P/Invoke**

Wrap `PowerEnumerate`, `PowerReadFriendlyName`, `PowerGetActiveScheme`, `PowerSetActiveScheme`, and `LocalFree`. Keep unsafe/native memory management inside this file only.

- [ ] **Step 3: Add a read-only reference host integration smoke test command**

The integration test lists schemes and confirms the currently active scheme without switching. A separate explicitly-invoked test may switch Balanced -> original active plan -> verify restoration; never leave the test machine on a different plan after completion.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test tests/PowerFlow.Windows.Tests/PowerFlow.Windows.Tests.csproj
git add src/PowerFlow.Windows/Power tests/PowerFlow.Windows.Tests/Power
git commit -m "feat: add verified Windows power-plan control"
```

---

### Task 4: Low-overhead CPU activity and dashboard-only telemetry

**Files:**
- Create: `src/PowerFlow.Windows/Activity/IActivitySource.cs`
- Create: `src/PowerFlow.Windows/Activity/SystemTimesActivitySource.cs`
- Create: `src/PowerFlow.Windows/Activity/DashboardTelemetrySource.cs`
- Create: `tests/PowerFlow.Windows.Tests/Activity/SystemTimesActivitySourceTests.cs`

**Interfaces:**
- Produces: `ActivitySample(double CpuPercent, DateTimeOffset At)` from `GetSystemTimes` snapshots.
- Produces: `DashboardTelemetry` with optional package watts and average MHz; it is callable only while dashboard is visible/diagnostics are explicitly requested.

- [ ] **Step 1: Write deterministic CPU delta tests**

Inject synthetic `FILETIME` snapshots and verify 0%, 50%, and 100% CPU calculations, counter wrap protection, and invalid/zero interval handling.

- [ ] **Step 2: Implement CPU source using `GetSystemTimes`**

The source itself has no timer. The controller schedules it at 2-second intervals only outside Game Latch. Avoid `PerformanceCounter`/WMI for the core CPU loop.

- [ ] **Step 3: Implement optional dashboard telemetry**

Use the Windows Energy Meter RAPL package counter if present and `CallNtPowerInformation(ProcessorInformation)` for frequency. Failure to access either metric returns `null`; it must never block policy switching.

- [ ] **Step 4: Add timing instrumentation**

Expose sample-duration diagnostics so overhead tests can prove each closed-dashboard sample is short and bounded.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test PowerFlow.sln
git add src/PowerFlow.Windows/Activity tests/PowerFlow.Windows.Tests/Activity
git commit -m "feat: add low-overhead CPU activity source"
```

---

### Task 5: Game detection and hard lifecycle latch

**Files:**
- Create: `src/PowerFlow.Windows/Games/GameProcess.cs`
- Create: `src/PowerFlow.Windows/Games/IGameLifecycleMonitor.cs`
- Create: `src/PowerFlow.Windows/Games/GameLifecycleMonitor.cs`
- Create: `src/PowerFlow.Windows/Games/GameHeuristic.cs`
- Create: `src/PowerFlow.Windows/Foreground/ForegroundWindowProbe.cs`
- Create: `tests/PowerFlow.Windows.Tests/Games/GameLifecycleMonitorTests.cs`

**Interfaces:**
- Produces: `GameDetected(GameProcess process, string reason)`, `GameProcessAdded`, `GameLatchReleased` events.
- `GameProcess` contains PID, executable path, parent PID when known, start time, and rule source.
- Once latched, the monitor stops heuristic CPU/GPU checks and waits on tracked process handles / `Process.Exited` events.

- [ ] **Step 1: Write lifecycle tests with fake processes**

Cover explicit-rule match, launcher -> child handoff, parent launcher remaining after game child exits, multiple related game processes, PID reuse protection via start time, process exit releasing latch only when tracked game set is empty, and bounded grace period on uncertain handoff.

- [ ] **Step 2: Implement explicit rule matching and foreground probe**

Use executable full path first, then normalized filename rule. `ForegroundWindowProbe` uses `GetForegroundWindow`, `GetWindowThreadProcessId`, and window bounds to determine whether a candidate occupies >=90% of the monitor working bounds.

- [ ] **Step 3: Implement conservative unlisted-game heuristic outside latch**

Only consider a process after it remains foreground/full-screen for two consecutive 2-second controller samples. GPU evidence is optional enrichment; absence of reliable per-process GPU data must not create a tight polling loop. Candidate promotion reason must state whether it came from an explicit rule or heuristic.

- [ ] **Step 4: Implement event-driven process observation**

Use a bounded `ManagementEventWatcher` subscription for process start/stop only while not latched. Once a game is latched, stop the watcher if tracked process handles are sufficient and use `EnableRaisingEvents`/registered process waits. Benchmark the watcher in Task 9; if it materially increases WmiPrvSE/privileged CPU, replace it before acceptance rather than weakening the overhead gate.

- [ ] **Step 5: Run lifecycle tests and commit**

```powershell
dotnet test tests/PowerFlow.Windows.Tests/PowerFlow.Windows.Tests.csproj
git add src/PowerFlow.Windows/Games src/PowerFlow.Windows/Foreground tests/PowerFlow.Windows.Tests/Games
git commit -m "feat: add hard game lifecycle latch"
```

---

### Task 6: Controller orchestration, precedence, and timer suppression

**Files:**
- Create: `src/PowerFlow.App/PowerFlow.App.csproj`
- Create: `src/PowerFlow.App/Controller/ControllerSnapshot.cs`
- Create: `src/PowerFlow.App/Controller/PowerFlowController.cs`
- Create: `tests/PowerFlow.App.Tests/PowerFlow.App.Tests.csproj`
- Create: `tests/PowerFlow.App.Tests/Controller/PowerFlowControllerTests.cs`

**Interfaces:**
- Produces: `PowerFlowController.StartAsync()`, `StopAsync()`, `SetManualStateAsync(PowerState?)`, `ReleaseManualLatchAsync()`, `SnapshotChanged`.
- `ControllerSnapshot` is the UI contract: current plan/state, reason, latch type, CPU demand, threshold progress, cooldown remaining, active trigger app, last transition, and diagnostics counters.

- [ ] **Step 1: Write orchestration tests with fake clock/sources**

Prove closed-dashboard sampling is >=2 seconds, Game Latch cancels the activity sampling timer, game process exit re-enables normal policy evaluation only after cooldown, manual latch disables demotion, transition precedence matches the spec, activation failure retains previous known state, and startup reads actual Windows plan before choosing any transition.

- [ ] **Step 2: Implement one cancellable controller loop**

Use `PeriodicTimer(TimeSpan.FromSeconds(2))` only in ordinary non-latched operation. Dispose/cancel it on Game Latch rather than letting it wake and return early. No secondary heartbeat timer.

- [ ] **Step 3: Wire decision -> verified power-plan activation -> snapshot**

Publish a changed state only after the Windows controller verifies the target GUID. Transition history is an in-memory bounded ring of 100 entries, not a database.

- [ ] **Step 4: Implement abnormal-start recovery**

On startup, if prior state was latched/uncertain, inspect current explicit game rules/processes. If intent is ambiguous, choose Balanced as the safe neutral state; never blindly force Power Saver after an abnormal restart.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test PowerFlow.sln
git add src/PowerFlow.App/Controller tests/PowerFlow.App.Tests
git commit -m "feat: orchestrate PowerFlow state transitions"
```

---

### Task 7: Tray-first shell and per-user startup

**Files:**
- Create: `src/PowerFlow.App/App.xaml`
- Create: `src/PowerFlow.App/App.xaml.cs`
- Create: `src/PowerFlow.App/Tray/TrayIconHost.cs`
- Create: `src/PowerFlow.App/Tray/TrayMenuCommands.cs`
- Create: `src/PowerFlow.Windows/Startup/StartupRegistration.cs`
- Create: `src/PowerFlow.App/Package.appxmanifest`

**Interfaces:**
- Tray menu commands: `Open Dashboard`, current-state status line, `Power Saver`, `Balanced`, `High Performance (Latch)`, `Release Performance Latch`, `Settings`, `Exit`.

- [ ] **Step 1: Build WinUI 3 app shell against Windows App SDK 2.4.0**

Target x64 and `net8.0-windows10.0.19041.0`. App startup must instantiate controller + tray icon but not `MainWindow` until requested.

- [ ] **Step 2: Implement `Shell_NotifyIconW` tray host**

Use a hidden message window only as required for tray callbacks. Icons communicate Saver/Balanced/Performance/Locked states without continuous animation. Double-click opens dashboard; right-click opens native menu.

- [ ] **Step 3: Wire manual latches and safe exit**

Manual High Performance menu choice creates a latch. Exit disposes process watchers, timers, native handles, and tray icon. App exit does not silently force a different Windows plan unless the user enabled an explicit exit-policy preference.

- [ ] **Step 4: Implement normal per-user startup registration**

Use `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for unpackaged dev builds and the supported packaged startup mechanism for packaged builds. No privileged scheduled task.

- [ ] **Step 5: Verify no popup/console startup and commit**

Launch from Explorer and startup command; verify tray appears without a dashboard/window. Exit and verify no PowerFlow process remains.

```powershell
git add src/PowerFlow.App src/PowerFlow.Windows/Startup
git commit -m "feat: add tray-first PowerFlow shell"
```

---

### Task 8: Motion-flow dashboard and rules/settings experience

**Files:**
- Create: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Create: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Create: `src/PowerFlow.App/Dashboard/FlowFieldControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/FlowFieldControl.xaml.cs`
- Create: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs`
- Create: `src/PowerFlow.App/Dashboard/Converters.cs`
- Create: `src/PowerFlow.App/Settings/RulesPage.xaml`
- Create: `src/PowerFlow.App/Settings/RulesPage.xaml.cs`
- Create: `src/PowerFlow.App/Settings/SettingsPage.xaml`
- Create: `src/PowerFlow.App/Settings/SettingsPage.xaml.cs`

**Interfaces:**
- Dashboard binds only to `ControllerSnapshot`; it does not own policy logic.
- Opening dashboard enables 1-second dashboard-only telemetry; closing it stops and disposes that telemetry loop.

- [ ] **Step 1: Build the three-destination flow layout**

Create a horizontal visual field with `Power Saver`, `Balanced`, and `Performance` anchors. Current state has visual gravity; threshold progress changes flow density/velocity; promotions visually travel toward the destination; cooldown visibly drains back. Avoid a grid of generic cards as the primary composition.

- [ ] **Step 2: Implement composition motion with bounded render lifetime**

Use WinUI Composition animations/XAML paths. Create animations when the window becomes visible; stop animations and release rendering callbacks on hide/close-to-tray. No hidden-window animation loop.

- [ ] **Step 3: Implement Reduced Motion**

Read the OS animation preference and user override. Reduced Motion replaces travel/particle motion with crossfade, gentle scale, and static threshold fill while preserving state/reason/cooldown clarity.

- [ ] **Step 4: Add live telemetry and explanation layer**

Show CPU %, optional package watts, MHz, active trigger app, exact transition reason, latch/cooldown badge, and last transitions. The user must be able to answer where/why/what-next without opening logs.

- [ ] **Step 5: Add rules/settings secondary views**

Allow `Remember current foreground app as Game`, explicit Balanced/Performance rule editing, thresholds/timings, plan mappings, resting-state safety toggle, startup preference, diagnostics, and Reduced Motion.

- [ ] **Step 6: Manual UI acceptance and commit**

Exercise Saver -> gathering -> Balanced, Game Latch, cooldown, manual Performance latch, and Reduced Motion. Capture screenshots for review but do not leave test helper processes running.

```powershell
git add src/PowerFlow.App/Dashboard src/PowerFlow.App/Settings
git commit -m "feat: add PowerFlow motion dashboard"
```

---

### Task 9: Performance harness, hygiene gates, and reference host acceptance

**Files:**
- Create: `src/PowerFlow.PerfHarness/PowerFlow.PerfHarness.csproj`
- Create: `src/PowerFlow.PerfHarness/Program.cs`
- Create: `scripts/Measure-PowerFlowOverhead.ps1`
- Create: `scripts/Verify-PowerFlowHygiene.ps1`

**Interfaces:**
- Perf harness emits machine-readable JSON with mode, duration, app CPU time, privileged CPU delta, wake/timer proxy metrics, disk-write delta, and controller sample counts.

- [ ] **Step 1: Create controller-off baseline measurement**

Measure reference host for at least 120 seconds with PowerFlow absent: Energy Meter package watts, total CPU, privileged CPU, PowerFlow process absent, and relevant WmiPrvSE CPU. Record median and p95 rather than one instantaneous sample.

- [ ] **Step 2: Measure dashboard-closed ordinary mode**

Run 120 seconds in the safe resting state. Acceptance: PowerFlow average CPU <=0.15% of total machine CPU, privileged CPU attributable to PowerFlow/WMI increase <=0.10 percentage points, and median package power increase <=2 W versus comparable controller-off baseline. If WMI process watching breaks this gate, replace that mechanism before continuing.

- [ ] **Step 3: Measure Game Latch closed mode**

Latch a harmless test process as a game for 120 seconds. Verify activity sample count stops increasing, dashboard telemetry sample count is zero, no demotion evaluations run, and Game Latch mode is equal to or quieter than ordinary closed mode.

- [ ] **Step 4: Measure dashboard-open mode**

Open dashboard for 120 seconds. Verify 1-second telemetry rate, smooth UI, no runaway allocations, and immediate cessation of dashboard telemetry after close.

- [ ] **Step 5: Verify game performance behavior**

Run a real game/test workload. Confirm High Performance engages before sustained gameplay, remains latched through menus/loading/pause/low CPU, releases only after tracked game processes are actually gone, and compare frame-time behavior with PowerFlow disabled. No measurable recurring frame-time spike may correlate with PowerFlow activity.

- [ ] **Step 6: Run hygiene verification**

`Verify-PowerFlowHygiene.ps1` checks: exactly one intended PowerFlow process, no orphan helper, no console host spawned by PowerFlow, no stale test process, no PowerFlow scheduled task, bounded logs/config only, and current power plan is explicitly reported at test end.

- [ ] **Step 7: Full verification and commit**

Run:

```powershell
dotnet test PowerFlow.sln -c Release
dotnet build PowerFlow.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts\Verify-PowerFlowHygiene.ps1
```

Expected: all tests/build/hygiene checks PASS; performance measurements meet gates. Commit evidence scripts and final fixes.

```powershell
git add src/PowerFlow.PerfHarness scripts
git commit -m "test: add PowerFlow performance and hygiene gates"
```

---

### Task 10: Packaging, install, and human-approval build

**Files:**
- Modify: `src/PowerFlow.App/PowerFlow.App.csproj`
- Create: `README.md`
- Create: `docs/acceptance/reference-host-acceptance.md`

**Interfaces:**
- Produces a local x64 installable build and an unpackaged dev build; neither may install a service.

- [ ] **Step 1: Produce Release x64 build**

Build a packaged WinUI 3 x64 app using Windows App SDK 2.4.0. Keep signing local/development appropriate; do not introduce cloud CI or GitHub publishing.

- [ ] **Step 2: Write operator documentation**

Document tray behavior, three states, game/manual latch semantics, rule creation, safety resting-state toggle, startup/exit behavior, config path, troubleshooting, and clean uninstall.

- [ ] **Step 3: Record acceptance evidence**

`docs/acceptance/reference-host-acceptance.md` records exact build commit, test totals, overhead baselines/results, game-latch verification, startup/hygiene checks, and explicitly states whether reference host's separate Power Saver stability issue is resolved or still blocks production use.

- [ ] **Step 4: Human approval gate**

Install/run the candidate on reference host with resting state set to Balanced if Power Saver stability is still unresolved. Present the motion dashboard and tray behavior for human approval. Do not enable auto-start or Power Saver resting mode permanently before approval.

- [ ] **Step 5: Final hygiene**

Remove test builds, temporary logs, stale processes, unused worktrees, and any staging artifacts. Verify exactly one intended installed/runtime instance and clean Git status before declaring the build ready.

- [ ] **Step 6: Commit release docs**

```powershell
git add README.md docs/acceptance src/PowerFlow.App/PowerFlow.App.csproj
git commit -m "docs: record PowerFlow release acceptance"
```

## Plan Self-Review

- Spec coverage: all state transitions, precedence, hard Game/Manual latches, low-overhead rules, game lifecycle, plan verification, atomic config, tray-first startup, motion UI, Reduced Motion, failure recovery, reference host safety, performance/hygiene acceptance, and non-production gate are mapped to tasks.
- Placeholder scan: no TBD/TODO/implement-later instructions remain.
- Type consistency: `PowerState`, `PolicyDecision`, `PowerFlowConfig`, `IPowerPlanController`, `IActivitySource`, `IGameLifecycleMonitor`, `PowerFlowController`, and `ControllerSnapshot` are introduced before downstream use.
- Scope: one product with separable/testable components; no unrelated fan/RGB/BIOS/power-plan editing work is included.
