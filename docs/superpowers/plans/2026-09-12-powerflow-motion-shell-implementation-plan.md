# PowerFlow Motion Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a mechanically stable, physically believable single-window PowerFlow shell that grows from the tray through Peek, Live, Dashboard, and Workspace, preserves observational Tension Shadow behavior, and installs to a stable per-user path with shortcuts.

**Architecture:** Separate native geometry authority, render-frame resize scheduling, semantic disclosure, and page navigation. A single `ShellMotionCoordinator` drives native bounds and composition progress using explicit physical material models; manual resize uses a coalesced lightweight frame path plus one settled semantic commit. Existing tray geometry is the canonical origin for tray-anchored transitions.

**Tech Stack:** .NET 8, WinUI 3 / Windows App SDK, `Microsoft.UI.Windowing.AppWindow`, Win32 tray APIs, Windows Composition, xUnit, PowerShell packaging/install scripts.

**Spec:** `docs/superpowers/specs/2026-09-12-powerflow-motion-shell-design.md`

## Global Constraints

- Current AUTO remains the only automatic actuator; Tension Shadow is observational only.
- Navigation must never resize or move the window.
- Tray→Peek must use the measured tray icon rectangle when available.
- Live, Dashboard, and Workspace must be moveable and resizeable.
- Manual resize must repaint continuously and coalesce semantic presentation updates.
- Automatic motion must use explicit rigid, compliant, or fluid material behavior; no random bounce or unrelated animation clocks.
- Compliant overshoot is bounded to 2.5% of travel or 6 logical px, whichever is smaller, with one overshoot lobe maximum.
- Fluid squash/stretch is bounded to ±3% scale.
- Reduced-motion mode preserves correct geometry with no overshoot.
- No perpetual tray animation while settled.
- Primary text remains at least 11 logical px; existing readability and contrast contracts remain in force.
- User data under `%LocalAppData%\PowerFlow` must survive application upgrade/uninstall by default.

---

### Task 1: Frame Ownership — Drag Surface and Navigation/Geometry Decoupling

**Files:**
- Create: `src/PowerFlow.App/Dashboard/ShellGeometryAuthority.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellSectionPolicy.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellFrameAuthorityTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/VisualCoherenceContractTests.cs`

**Interfaces:**
- Produces: `ShellGeometryAuthority.ShouldChangeBounds(ShellGeometryIntent intent)` and `ShellGeometryIntent` enum values `TrayOpen`, `Expand`, `Shrink`, `Workspace`, `FullScreen`, `UserResize`, `UserMove`, `Navigation`.
- Produces: `MainWindow.ConfigureCustomTitleBar()` and `MainWindow.UpdateDragRectangles()`.
- Navigation consumers must pass `ShellGeometryIntent.Navigation`, which never changes bounds.

- [ ] **Step 1: Write failing authority and drag-region tests**

```csharp
[Theory]
[InlineData(ShellGeometryIntent.Navigation, false)]
[InlineData(ShellGeometryIntent.Expand, true)]
[InlineData(ShellGeometryIntent.Shrink, true)]
[InlineData(ShellGeometryIntent.Workspace, true)]
public void GeometryAuthority_OnlyExplicitGeometryIntentsMayChangeBounds(ShellGeometryIntent intent, bool expected)
    => Assert.Equal(expected, ShellGeometryAuthority.ShouldChangeBounds(intent));

[Fact]
public void Navigation_DoesNotCallTransitionToChangeWindowState()
{
    var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
    var body = MethodBody(code, "private async Task NavigateToSectionAsync(string tag)");
    Assert.DoesNotContain("TransitionToAsync", body, StringComparison.Ordinal);
    Assert.DoesNotContain("MinimumState", body, StringComparison.Ordinal);
}

[Fact]
public void PinnedShell_RegistersVisibleHeaderAsCustomTitleBar()
{
    var code = Read("src", "PowerFlow.App", "Dashboard", "MainWindow.xaml.cs");
    Assert.Contains("ExtendsContentIntoTitleBar = true", code, StringComparison.Ordinal);
    Assert.Contains("SetTitleBar(DragSurface)", code, StringComparison.Ordinal);
    Assert.Contains("UpdateDragRectangles", code, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the targeted tests and verify RED**

Run:
`dotnet test tests/PowerFlow.App.Tests/PowerFlow.App.Tests.csproj -c Release --filter "FullyQualifiedName~ShellFrameAuthorityTests|FullyQualifiedName~VisualCoherenceContractTests"`

Expected: FAIL because the geometry authority/custom title bar do not exist and navigation still transitions shell state.

- [ ] **Step 3: Implement geometry authority and custom title bar**

```csharp
public enum ShellGeometryIntent { TrayOpen, Expand, Shrink, Workspace, FullScreen, UserResize, UserMove, Navigation }

public static class ShellGeometryAuthority
{
    public static bool ShouldChangeBounds(ShellGeometryIntent intent) => intent != ShellGeometryIntent.Navigation;
}
```

In `MainWindow.xaml`, add a named `DragSurface` behind the interactive header content. In `MainWindow.xaml.cs`, set `ExtendsContentIntoTitleBar = true`, call `SetTitleBar(DragSurface)`, and update title-bar input exclusion rectangles after layout using `InputNonClientPointerSource` or the Windows App SDK title-bar interactive-region API available to the target SDK. Buttons, navigation controls, sliders, and graph pointer surfaces must remain client-interactive.

Remove `ShellSectionPolicy.MinimumState()` from `NavigateToSectionAsync`; sections reflow/scroll in the current frame.

- [ ] **Step 4: Run targeted tests and source XAML parse**

Run the targeted command from Step 2 plus source XAML parse. Expected: PASS.

- [ ] **Step 5: Commit**

`git add src/PowerFlow.App/Dashboard tests/PowerFlow.App.Tests/Dashboard && git commit -m "Fix PowerFlow frame ownership"`

---

### Task 2: Render-Frame Resize Scheduler

**Files:**
- Create: `src/PowerFlow.App/Dashboard/ShellRenderScheduler.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellRenderSchedulerTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellResizeContractTests.cs`

**Interfaces:**
- Produces: `ShellRenderScheduler.SubmitSize(ShellLogicalSize size, DateTimeOffset at)`.
- Produces events/callbacks `FrameRequested(ShellLogicalSize)` and `ResizeSettled(ShellLogicalSize)`.
- Settled interval: 110 ms.

- [ ] **Step 1: Write RED scheduler tests**

```csharp
[Fact]
public void BurstOfNativeResizeSamples_CoalescesToLatestFrame()
{
    var scheduler = new ShellRenderScheduler(TimeSpan.FromMilliseconds(110));
    scheduler.SubmitSize(new(800, 500), T0);
    scheduler.SubmitSize(new(820, 520), T0.AddMilliseconds(3));
    scheduler.SubmitSize(new(840, 540), T0.AddMilliseconds(6));
    Assert.Equal(new ShellLogicalSize(840, 540), scheduler.ConsumePendingFrame());
}

[Fact]
public void SemanticCommit_RequiresResizeToSettle()
{
    var scheduler = new ShellRenderScheduler(TimeSpan.FromMilliseconds(110));
    scheduler.SubmitSize(new(900, 560), T0);
    Assert.False(scheduler.ShouldCommit(T0.AddMilliseconds(80)));
    Assert.True(scheduler.ShouldCommit(T0.AddMilliseconds(111)));
}
```

- [ ] **Step 2: Run targeted tests and verify RED**

Expected: FAIL because `ShellRenderScheduler` does not exist.

- [ ] **Step 3: Implement scheduler and lightweight resize path**

`OnAppWindowChanged` stores the latest logical size and requests one dispatcher/composition frame. The frame callback invokes a new `ApplyInteractiveResizeFrame(size)` that only updates continuous geometry/progress and chart viewport. A single `CommitResizePresentation(size)` runs after 110 ms of no new size event and performs endpoint `Visibility`, navigation-mode, and semantic presentation changes.

Do not call the existing full `ApplyShellLayout` for every native resize sample.

- [ ] **Step 4: Run scheduler, responsive density, timeline, and visual-coherence tests**

Expected: PASS with no existing responsive-layout regression.

- [ ] **Step 5: Commit**

`git add src/PowerFlow.App/Dashboard tests/PowerFlow.App.Tests/Dashboard && git commit -m "Coalesce PowerFlow resize rendering"`

---

### Task 3: Physical Motion Model

**Files:**
- Create: `src/PowerFlow.App/Dashboard/MotionMaterial.cs`
- Create: `src/PowerFlow.App/Dashboard/PhysicalMotionCurve.cs`
- Create: `src/PowerFlow.App/Dashboard/ShellMotionCoordinator.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellMotionPolicy.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellTransitionGeometry.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/PhysicalMotionCurveTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellMotionCoordinatorTests.cs`

**Interfaces:**
- Produces `enum MotionMaterial { Rigid, Compliant, Fluid }`.
- Produces `MotionSample(double Progress, double Velocity, double SecondaryScale)`.
- Produces `PhysicalMotionCurve.Sample(MotionMaterial material, double normalizedTime, double initialVelocity = 0)`.
- Produces coordinator state with current bounds, target bounds, material, progress, and velocity.

- [ ] **Step 1: Write RED physical-contract tests**

```csharp
[Theory]
[InlineData(MotionMaterial.Rigid)]
[InlineData(MotionMaterial.Fluid)]
public void NativeShellProgress_IsMonotonic(MotionMaterial material)
{
    var samples = Enumerable.Range(0, 101).Select(i => PhysicalMotionCurve.Sample(material, i / 100d)).ToArray();
    Assert.All(samples.Zip(samples.Skip(1)), pair => Assert.True(pair.Second.Progress >= pair.First.Progress));
}

[Fact]
public void CompliantOvershoot_IsBoundedAndSingleLobed()
{
    var samples = Enumerable.Range(0, 201).Select(i => PhysicalMotionCurve.Sample(MotionMaterial.Compliant, i / 200d)).ToArray();
    Assert.InRange(samples.Max(x => x.Progress), 1d, 1.025d);
    Assert.True(CountCrossings(samples.Select(x => x.Progress - 1d)) <= 2);
}

[Fact]
public void FluidSecondaryScale_IsBounded()
{
    var samples = Enumerable.Range(0, 101).Select(i => PhysicalMotionCurve.Sample(MotionMaterial.Fluid, i / 100d)).ToArray();
    Assert.All(samples, s => Assert.InRange(s.SecondaryScale, .97d, 1.03d));
}
```

- [ ] **Step 2: Verify RED**

Expected: FAIL because material/curve/coordinator types do not exist.

- [ ] **Step 3: Implement deterministic physical curves**

Rigid uses a monotonic critically damped response; Fluid uses a monotonic shell-progress curve plus bounded perpendicular squash/stretch; Compliant uses one underdamped internal-element response with capped overshoot. Clamp final sample exactly to progress 1, velocity 0, scale 1.

- [ ] **Step 4: Implement interruption continuity**

Coordinator retargeting captures current interpolated bounds and velocity; the next transition starts from those values instead of the old semantic endpoint. Add tests for mid-flight grow→shrink and grow→larger-target retargeting.

- [ ] **Step 5: Run motion and existing shell-motion tests; commit**

`git add src/PowerFlow.App/Dashboard tests/PowerFlow.App.Tests/Dashboard && git commit -m "Add physical shell motion model"`

---

### Task 4: Experience State Model — Peek, Live, Dashboard, Workspace

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/PowerFlowShellState.cs`
- Modify: `src/PowerFlow.App/Dashboard/PowerFlowShellLayout.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellPresentationProfile.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellStateTransition.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellTransitionGeometry.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/PowerFlowShellLayoutTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellStateTransitionTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellTransitionGeometryTests.cs`

**Interfaces:**
- Public product mapping: existing Glance→Peek, Compact→Live, Expanded→Dashboard.
- Add `Workspace` as a distinct pinned state before optional `FullScreen`.
- Workspace default logical target: 1360×860, clamped to monitor work area.

- [ ] **Step 1: Write RED tests for Workspace and navigation independence**

Assert Tray single click/hover targets Peek semantics, surface click targets Live, Expand from Live targets Dashboard, Expand from Dashboard targets Workspace, and FullScreen requires an explicit fullscreen command.

- [ ] **Step 2: Verify RED**

Expected: missing Workspace state/transition.

- [ ] **Step 3: Add Workspace state and geometry**

Preserve internal enum names for Peek/Live/Dashboard if renaming would cause unnecessary churn, but add Workspace explicitly and expose product labels through a projection. `ShellTransitionGeometry.TargetBounds` must preserve user-centered/current-window anchoring for pinned state growth after the first tray-origin transition.

- [ ] **Step 4: Run state/layout/geometry tests; commit**

`git commit -am "Add PowerFlow workspace presentation state"`

---

### Task 5: Integrate Motion Coordinator with Native Bounds and Semantic Morph

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/SemanticMorphContractTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ShellMotionIntegrationTests.cs`

**Interfaces:**
- `MainWindow.TransitionToAsync` delegates animation timing/retargeting to `ShellMotionCoordinator`.
- Native bounds consume monotonic rigid/fluid shell progress.
- Header/timeline/navigation/control details consume material-specific child progress derived from the same transition sample.

- [x] **Step 1: Write RED integration tests**

Require a single coordinator field, forbid independent presentation timers for shell motion, and assert transition frames take one `MotionSample` source.

- [x] **Step 2: Replace `DispatcherQueueTimer` shell animation with coordinator-driven render frames**

Use one render-frame cadence. Native bounds never use compliant overshoot. Internal cards may use compliant child progress.

- [x] **Step 3: Test reduced motion and interrupted transitions**

Reduced motion reaches identical target geometry with zero overshoot. Mid-flight retargeting begins from current geometry.

- [x] **Step 4: Run full motion/layout test family; commit**

`git commit -am "Drive PowerFlow shell from one motion coordinator"`

---

### Task 6: Tray-Origin Peek and Efficient Swoop Motion

**Files:**
- Create: `src/PowerFlow.App/Tray/TrayIconMotionPolicy.cs`
- Create: `src/PowerFlow.App/Tray/TrayIconFrameCache.cs`
- Modify: `src/PowerFlow.App/Tray/TrayIconHost.cs`
- Modify: `src/PowerFlow.App/App.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellTransitionGeometry.cs`
- Test: `tests/PowerFlow.App.Tests/Tray/TrayIconMotionPolicyTests.cs`
- Test: `tests/PowerFlow.App.Tests/Tray/TrayMotionIntegrationTests.cs`

**Interfaces:**
- `TrayIconMotionPolicy.Decide(previousSnapshot, currentSnapshot, manualMode)` returns a finite animation intent or `None`.
- Frame cache owns reusable HICON handles and exposes no background timer.
- Tray icon rectangle remains the source origin for Peek.

- [ ] **Step 1: Write RED finite-animation tests**

Assert no animation for equivalent settled snapshots, finite animation for zone/profile change, distinct held/manual intent, and no repeating timer requirement.

- [ ] **Step 2: Implement cached short-frame animation**

Reuse the existing PowerFlow icon artwork and derive/carry a small finite set of swoop-state frames. Animate at 8–12 fps for 350–700 ms only when policy state changes. Stop the timer and leave a static final icon.

- [ ] **Step 3: Make Tray→Peek motion originate at actual `Shell_NotifyIconGetRect` bounds**

Fallback to the observed hover rectangle only when the shell rectangle is unavailable.

- [ ] **Step 4: Run tray policy/integration tests; commit**

`git commit -am "Animate PowerFlow tray state and anchor Peek"`

---

### Task 7: Progressive Disclosure Across Live, Dashboard, Workspace

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/PowerFlowShellLayout.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Modify: `src/PowerFlow.App/Settings/RulesPage.xaml`
- Modify: `src/PowerFlow.App/Settings/SettingsPage.xaml`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ProgressivePresentationContractTests.cs`
- Test: `tests/PowerFlow.App.Tests/Dashboard/ReadabilityLayoutContractTests.cs`

**Interfaces:**
- Disclosure depth is a function of available logical width/height and explicit presentation state, never selected page.
- Section content scrolls/reflows in smaller pinned frames.

- [ ] **Step 1: Write RED product-state visibility tests**

Assert Peek is view-first, Live contains the primary timeline and compact governor control, Dashboard exposes navigation/richer context, and Workspace exposes the primary controls without drawers.

- [ ] **Step 2: Implement continuous detail progress**

Use opacity/clip/offset while transitioning. Defer `Visibility.Collapsed` until a detail reaches zero opacity or settled semantic commit.

- [ ] **Step 3: Verify navigation does not change native bounds across all four sections**

Add source contract plus live acceptance script later in Task 9.

- [ ] **Step 4: Run layout/readability/theme tests; commit**

`git commit -am "Add progressive PowerFlow workspace disclosure"`

---

### Task 8: Reintegrate Tension Shadow into Stable Motion Shell

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PerformanceTimelineControl.xaml.cs`
- Modify: `tests/PowerFlow.App.Tests/Controller/TensionShadowWiringTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/TensionShadowSurfaceContractTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/TensionShadowOverlayTests.cs`

**Interfaces:**
- Current AUTO remains `APPLIED`.
- Tension remains `SHADOW` / `WOULD USE`.
- Peek/Live may expose one tension control; deeper states expose richer explanation/evidence.

- [ ] **Step 1: Repair the two existing candidate visual-contract failures without weakening contracts**

Use text size >=11 and a subordinate translucent ghost treatment that does not introduce generic dashed policy clutter.

- [ ] **Step 2: Add state-specific shadow presentation tests**

Verify Peek/Live stay concise and Workspace exposes rich comparison.

- [ ] **Step 3: Re-run shadow authority audit**

Fail if shadow code references actuation/profile/Windows controller APIs or if App routes `shadowEvaluation` to adaptive actuation.

- [ ] **Step 4: Commit**

`git commit -am "Integrate Tension Shadow with Motion Shell"`

---

### Task 9: Native UI Acceptance — Move, Resize, Repaint, Navigation, Motion

**Files:**
- Create: `docs/acceptance/powerflow-motion-shell-2026-09-12.md`
- Add/modify acceptance scripts only under `tools/validation/` if reusable; do not leave ad-hoc scripts in the repo root.

**Interfaces:**
- Evidence must come from the exact Release candidate executable that would be published.

- [ ] **Step 1: Run full automated gate**

Run Core, Windows, App Release tests; full Release build; source XAML parse; `git diff --check`; shadow authority audit.

- [ ] **Step 2: Launch isolated Release candidate and test native dragging**

Use Win32 window bounds before/after a real header drag. Verify controls remain clickable and bounds move without resize.

- [ ] **Step 3: Run continuous manual-resize sweep**

Exercise at least 760×440, 820×500, 860×520, 900×560, 1100×680, 1280×800, and 1360×860 plus intermediate continuous dragging. Capture frame samples at <=16 ms where possible; require continuous visual repaint, no stale/blank region, and zero visible clipping.

- [ ] **Step 4: Verify navigation geometry invariance**

At Live, Dashboard, and Workspace sizes, record native bounds, select Live/Workloads/Baseline/Settings, and assert every selection preserves exact native bounds.

- [ ] **Step 5: Measure automatic motion**

Measure Tray→Peek, Peek→Live, Live→Dashboard, Dashboard→Workspace and reverse paths. Record native bounds samples, distinct-frame count, maximum step, reversal count, and endpoint error. Require zero native-direction reversals for monotonic transitions and physical-contract limits for internal compliant/fluid motion.

- [ ] **Step 6: Capture final screenshots and acceptance record**

Include hashes, dimensions, runtime, test counts, motion metrics, resize results, drag result, navigation invariance, and any explicitly separate known defects.

- [ ] **Step 7: Commit acceptance evidence**

`git add docs/acceptance tools/validation && git commit -m "Record Motion Shell acceptance"`

---

### Task 10: Per-User Installation and Shortcuts

**Files:**
- Create: `tools/install/Install-PowerFlow.ps1`
- Create: `tools/install/Uninstall-PowerFlow.ps1`
- Create: `tools/install/Build-PowerFlowDistribution.ps1`
- Test: `tests/PowerFlow.App.Tests/Startup/InstallLayoutContractTests.cs`
- Modify: `README.md` only if needed for the one supported install command.

**Interfaces:**
- Publish/install root: `%LocalAppData%\Programs\PowerFlow`.
- User-data root remains `%LocalAppData%\PowerFlow` and is not deleted on upgrade/uninstall by default.
- Start Menu shortcut: `%AppData%\Microsoft\Windows\Start Menu\Programs\PowerFlow.lnk`.
- Desktop shortcut: `%UserProfile%\Desktop\PowerFlow.lnk` unless `-NoDesktopShortcut`.
- Shortcut target: installed `PowerFlow.App.exe --dashboard`.

- [ ] **Step 1: Write RED install-layout tests**

Contract-test stable paths, `--dashboard` shortcut arguments, preservation of user-data path, and optional desktop shortcut.

- [ ] **Step 2: Implement distribution build**

`dotnet publish src/PowerFlow.App/PowerFlow.App.csproj -c Release -r win-x64 --self-contained true` to a staging directory, validate expected executable/assets, and package the payload beside the installer scripts.

- [ ] **Step 3: Implement idempotent per-user install/upgrade**

Stop only an existing installed PowerFlow process after explicit installer invocation, copy payload atomically via staging directory, create/update Start Menu and Desktop shortcuts with `WScript.Shell`, preserve `%LocalAppData%\PowerFlow`, and relaunch from installed path when requested.

- [ ] **Step 4: Implement uninstall**

Remove shortcuts and installed payload. Preserve data by default; add explicit `-RemoveUserData` for full cleanup.

- [ ] **Step 5: Test fresh install, upgrade, launch, and uninstall**

Verify shortcuts resolve to installed path, launch tray + dashboard, upgrade preserves config, and uninstall removes only installed program/shortcuts.

- [ ] **Step 6: Commit**

`git add tools/install tests/PowerFlow.App.Tests/Startup README.md && git commit -m "Add PowerFlow per-user installer"`

---

### Task 11: Publish and Operational Handoff

**Files:**
- No new production files unless qualification finds a defect.

**Interfaces:**
- Published `master`, `origin/master`, and fresh remote ref must match.
- Temporary worktrees are removed only after the installed/published build is confirmed healthy.

- [ ] **Step 1: Fetch remote and integrate without force-push**
- [ ] **Step 2: Run the definitive full gate after integration**
- [ ] **Step 3: Push `master` and verify local/remote 0 ahead / 0 behind**
- [ ] **Step 4: Install the published candidate to `%LocalAppData%\Programs\PowerFlow`**
- [ ] **Step 5: Launch through the Start Menu/Desktop shortcut and verify process path, tray icon, dashboard motion, drag, resize, and no startup error**
- [ ] **Step 6: Leave the installed stable app running; remove isolated worktree after verification**