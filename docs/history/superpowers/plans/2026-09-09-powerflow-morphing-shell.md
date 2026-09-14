> **SUPERSEDED BY REFERENCE-FAITHFUL IA EXPERIMENT — DO NOT CONTINUE THIS PLAN.**
> The approved 2026-09-09 corrected design in `../specs/2026-09-09-powerflow-morphing-shell-design.md` replaces the information architecture assumed here. A new implementation plan must be written only after that corrected spec passes user review.
# PowerFlow Morphing Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace PowerFlow's separate tray popup/dashboard surfaces with one polished WinUI shell that grows from the tray through Glance -> Compact -> Expanded while preserving shared visual anchors and matching the supplied navy/cyan cockpit direction.

**Architecture:** `MainWindow` becomes the only visual PowerFlow window. Pure `PowerFlowShellLayout` and `ShellTransitionGeometry` models calculate density and geometry; `MainWindow` applies those profiles and animates a single HWND with `AppWindow.MoveAndResize`. `TrayIconHost` reports hover/single-click/double-click intent and `App` routes all visual requests to the same shell instance.

**Tech Stack:** C#/.NET 8, WinUI 3 / Windows App SDK 2.4, AppWindow/Win32 tray APIs, xUnit, existing PowerFlow theme resources and telemetry/controller models.

**Spec:** `docs/superpowers/specs/2026-09-09-powerflow-morphing-shell-design.md`

## Global Constraints

- TDD is mandatory for every behavior change: RED must be observed before production code.
- One PowerFlow visual HWND only; `TrayHoverWindow` must be removed after parity is proven.
- Canonical shell states: Hidden, Glance 320x176, Compact 760x440, Expanded 1280x800, optional FullScreen extension.
- Tray single left click opens/pins Glance; Glance click grows the same HWND to Compact; Expand grows the same HWND to Expanded.
- Window transitions animate position and size together with `AppWindow.MoveAndResize` and remain anchored to the actual tray-icon/work-area geometry.
- Shared identity/state/telemetry/trajectory/policy controls remain persistent element instances wherever practical.
- Reference-image visual language: near-black navy, glass/Mica depth, cyan edge accents, semantic green/blue/orange/purple mode colors, rounded panels, restrained glow, graph-first hierarchy.
- Never invent telemetry PowerFlow does not already collect.
- Explicit visible font sizes must be at least 11 px.
- Reduced Motion must preserve behavior while removing or minimizing geometry/content animation.
- Rules and Settings route through the same shell; Compact may grow to Expanded when those sections need room.
- README is public product documentation, not release playback; retain only a short AI/TDD note and truthful build/run instructions.
- Do not add installer/package work or change power-plan policy in this plan.

---

### Task 1: Pure shell states, layout profiles, and tray-anchored geometry

**Files:**
- Create: `src/PowerFlow.App/Dashboard/PowerFlowShellState.cs`
- Create: `src/PowerFlow.App/Dashboard/PowerFlowShellLayout.cs`
- Create: `src/PowerFlow.App/Dashboard/ShellTransitionGeometry.cs`
- Delete after migration: `src/PowerFlow.App/Dashboard/DashboardPresentationMode.cs`
- Delete after migration: `src/PowerFlow.App/Dashboard/DashboardResponsiveLayout.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/PowerFlowShellLayoutTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/ShellTransitionGeometryTests.cs`
- Replace after green: `tests/PowerFlow.App.Tests/Dashboard/DashboardResponsiveLayoutTests.cs`

**Interfaces:**
- Produces `PowerFlowShellState { Hidden, Glance, Compact, Expanded, FullScreen }`.
- Produces `ShellActivationMode { TransientNoActivate, PinnedActive }`.
- Produces `PowerFlowShellLayout.Resolve(int width, int height, PowerFlowShellState requestedState, string section)` -> `PowerFlowShellLayoutProfile`.
- Produces `ShellTransitionGeometry.TargetBounds(TrayRect tray, TrayRect workArea, RectInt32 current, PowerFlowShellState target)` -> `RectInt32`.
- Produces `ShellTransitionGeometry.Interpolate(RectInt32 start, RectInt32 end, double progress)` -> `RectInt32`.

- [ ] **Step 1: Write failing shell-layout tests**

```csharp
[Fact]
public void Resolve_Glance_IsMinimalAndNavigationFree()
{
    var p = PowerFlowShellLayout.Resolve(320, 176, PowerFlowShellState.Glance, "flow");
    Assert.Equal(PowerFlowShellState.Glance, p.State);
    Assert.False(p.ShowNavigationRail);
    Assert.False(p.ShowModeCards);
    Assert.True(p.ShowGlanceTelemetry);
    Assert.True(p.GraphHeight <= 70);
}

[Fact]
public void Resolve_Expanded_UsesReferenceCockpitDensity()
{
    var p = PowerFlowShellLayout.Resolve(1280, 800, PowerFlowShellState.Expanded, "flow");
    Assert.True(p.ShowNavigationRail);
    Assert.True(p.ShowModeCards);
    Assert.True(p.ShowLiveStatsPanel);
    Assert.True(p.ShowLowerContextPanels);
    Assert.True(p.GraphHeight >= 360);
}

[Fact]
public void Resolve_Expanded_RemainsFluidInsideSameState()
{
    var small = PowerFlowShellLayout.Resolve(980, 620, PowerFlowShellState.Expanded, "flow");
    var large = PowerFlowShellLayout.Resolve(1320, 820, PowerFlowShellState.Expanded, "flow");
    Assert.Equal(PowerFlowShellState.Expanded, small.State);
    Assert.Equal(PowerFlowShellState.Expanded, large.State);
    Assert.True(large.GraphHeight > small.GraphHeight);
    Assert.True(large.PanelGap > small.PanelGap);
}
```

- [ ] **Step 2: Run the new layout tests and verify RED**

Run:
```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --filter FullyQualifiedName~PowerFlowShellLayoutTests --disable-build-servers
```
Expected: compilation failure because `PowerFlowShellLayout` / `PowerFlowShellState` do not exist.

- [ ] **Step 3: Write failing geometry tests**

```csharp
[Fact]
public void TargetBounds_Glance_SitsAboveTrayAndInsideWorkArea()
{
    var tray = new TrayRect(1800, 1030, 1840, 1070);
    var work = new TrayRect(0, 0, 1920, 1040);
    var target = ShellTransitionGeometry.TargetBounds(tray, work, new RectInt32(0,0,1,1), PowerFlowShellState.Glance);
    Assert.Equal(320, target.Width);
    Assert.Equal(176, target.Height);
    Assert.True(target.X >= work.Left && target.X + target.Width <= work.Right);
    Assert.True(target.Y + target.Height <= tray.Top);
}

[Theory]
[InlineData(0.0)]
[InlineData(0.25)]
[InlineData(0.5)]
[InlineData(0.75)]
[InlineData(1.0)]
public void Interpolate_IsMonotonicAndHasExactEndpoints(double progress)
{
    var start = new RectInt32(1600, 900, 320, 176);
    var end = new RectInt32(700, 300, 760, 440);
    var frame = ShellTransitionGeometry.Interpolate(start, end, progress);
    Assert.InRange(frame.Width, 320, 760);
    Assert.InRange(frame.Height, 176, 440);
    if (progress == 0) Assert.Equal(start, frame);
    if (progress == 1) Assert.Equal(end, frame);
}
```

- [ ] **Step 4: Run geometry tests and verify RED**

Run the geometry filter; expected failure because the model is missing.

- [ ] **Step 5: Implement minimal pure models**

Implement the enums/profile plus exact canonical sizes and clamped work-area placement. `Interpolate` must clamp progress to `[0,1]`, use deterministic rounded integer interpolation, and have exact endpoints.

- [ ] **Step 6: Run layout + geometry tests and verify GREEN**

Run both filters, then the existing presentation-contract tests to expose migration work without changing unrelated behavior.

- [ ] **Step 7: Commit**

```powershell
git add src/PowerFlow.App/Dashboard tests/PowerFlow.App.Tests/Dashboard
git commit -m "feat: add PowerFlow shell layout geometry"
```

---

### Task 2: Tray single-click semantics and shell intent model

**Files:**
- Modify: `src/PowerFlow.App/Tray/TrayIconHost.cs`
- Create: `src/PowerFlow.App/Tray/TrayInteractionIntent.cs`
- Modify: `tests/PowerFlow.App.Tests/Shell/TrayPopupAndPickerContractTests.cs`
- Create: `tests/PowerFlow.App.Tests/Shell/TrayInteractionIntentTests.cs`

**Interfaces:**
- `TrayIconHost` raises a typed event for `Hover`, `SingleClick`, `DoubleClick`, and existing context-menu commands.
- Single click does not directly create/open a window; `App` owns that response later.

- [ ] **Step 1: Write failing tests for tray intent**

```csharp
[Theory]
[InlineData(0x0202, TrayInteractionKind.SingleClick)] // WM_LBUTTONUP
[InlineData(0x0203, TrayInteractionKind.DoubleClick)]
public void Project_MapsLeftMouseMessages(uint message, TrayInteractionKind expected)
    => Assert.Equal(expected, TrayInteractionIntent.Project(message));
```

Add a source-contract assertion that `TrayIconHost` handles `WmLButtonUp` and no longer requires double click for ordinary open.

- [ ] **Step 2: Run tests and verify RED**

Expected failure: single-click intent type/constant missing.

- [ ] **Step 3: Implement intent projection and event**

Add `WmLButtonUp = 0x0202`. Raise `InteractionRequested` for hover/single/double. Preserve right-click context menu and current menu commands.

- [ ] **Step 4: Verify GREEN and run all tray tests**

Run `FullyQualifiedName~Tray` and `FullyQualifiedName~TrayInteractionIntentTests`.

- [ ] **Step 5: Commit**

```powershell
git add src/PowerFlow.App/Tray tests/PowerFlow.App.Tests/Shell
git commit -m "feat: add tray single click shell intent"
```

---

### Task 3: One-window lifecycle and Glance/Compact/Expanded state transitions

**Files:**
- Modify: `src/PowerFlow.App/App.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Startup/LaunchIntent.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/ShellStateTransitionTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/ProgressivePresentationContractTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Startup/LaunchIntentTests.cs`

**Interfaces:**
- `MainWindow.ShowShellAsync(PowerFlowShellState state, ShellActivationMode activation, TrayRect? trayAnchor, TrayRect? workArea, string section = "flow")`.
- `MainWindow.TransitionToAsync(PowerFlowShellState state, ShellActivationMode activation, bool animate)`.
- `App` owns exactly one `_shellWindow`; all hover/click/dashboard/settings routes call it.
- Glance surface tap invokes `TransitionToAsync(Compact, PinnedActive, true)`.

- [ ] **Step 1: Write failing pure state-transition tests**

Create a small pure `ShellStateTransition` policy if needed so transitions are testable without WinUI:

```csharp
[Theory]
[InlineData(PowerFlowShellState.Hidden, ShellInteraction.TraySingleClick, PowerFlowShellState.Glance)]
[InlineData(PowerFlowShellState.Glance, ShellInteraction.SurfaceClick, PowerFlowShellState.Compact)]
[InlineData(PowerFlowShellState.Compact, ShellInteraction.Expand, PowerFlowShellState.Expanded)]
[InlineData(PowerFlowShellState.Expanded, ShellInteraction.Collapse, PowerFlowShellState.Compact)]
public void Next_UsesGrowShrinkContract(...)
```

- [ ] **Step 2: Write failing one-window source-contract tests**

Assert:
- `App.xaml.cs` contains `_shellWindow` and does not instantiate `new TrayHoverWindow`.
- `MainWindow.xaml` contains named roots for Glance/Compact/Expanded density inside the same XAML tree.
- `MainWindow.xaml.cs` contains `AppWindow.MoveAndResize`.
- `ShowShellAsync` accepts `ShellActivationMode`.

- [ ] **Step 3: Run tests and verify RED**

Expected failures: `App` still owns `_dashboardWindow` + `_trayHoverWindow`; shell methods are absent.

- [ ] **Step 4: Implement one-window lifecycle minimally**

Create/reuse one `MainWindow`. Route hover to transient Glance, tray single click to pinned Glance, double click/secondary `--dashboard` to Compact, `--fullscreen` to FullScreen, settings to Expanded/Settings. Keep current controller/recorder subscriptions and hidden telemetry behavior.

- [ ] **Step 5: Implement no-activate/active chrome switching**

When `TransientNoActivate`, apply `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, show without stealing focus, and hide via existing hover grace. When pinned, remove `WS_EX_NOACTIVATE`, activate the same HWND, and stop transient auto-hide.

- [ ] **Step 6: Implement canonical geometry transitions**

Use `ShellTransitionGeometry.TargetBounds` and `Interpolate`; on each timer frame call `AppWindow.MoveAndResize(frame)` and apply the current layout profile. Preserve same HWND throughout.

- [ ] **Step 7: Verify GREEN**

Run shell transition, presentation, startup, tray, and single-instance tests. Build Release.

- [ ] **Step 8: Commit**

```powershell
git add src/PowerFlow.App tests/PowerFlow.App.Tests
git commit -m "feat: unify PowerFlow into one morphing shell"
```

---

### Task 4: Reference-inspired cockpit composition with persistent shared anchors

**Files:**
- Modify: `src/PowerFlow.App/App.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/TrajectoryControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/TrajectoryControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/TelemetryGraphControl.xaml`
- Modify if needed: `src/PowerFlow.App/Dashboard/TelemetryGraphControl.xaml.cs`
- Modify: `tests/PowerFlow.App.Tests/Shell/ReadabilityLayoutContractTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/ReferenceCockpitContractTests.cs`

**Interfaces:**
- One persistent `Trajectory` control instance across Glance/Compact/Expanded.
- One persistent state identity/mode selector host and one persistent telemetry host whose presentation density changes through the layout profile.
- `PowerFlowShellLayoutProfile` exposes navigation width, mode-card density, graph height, live-stats visibility, lower-panel visibility, panel gaps, corner radius, and font scale.

- [ ] **Step 1: Write failing visual/source contract tests**

Assert named anchors exist once in `MainWindow.xaml`:

```csharp
Assert.Equal(1, Count(xaml, "x:Name=\"Trajectory\""));
Assert.Contains("x:Name=\"ModeSelectorHost\"", xaml);
Assert.Contains("x:Name=\"LiveStatsPanel\"", xaml);
Assert.Contains("x:Name=\"NavigationRail\"", xaml);
Assert.Contains("PowerFlowSaverAccentBrush", appXaml);
Assert.Contains("PowerFlowBalancedAccentBrush", appXaml);
Assert.Contains("PowerFlowPerformanceAccentBrush", appXaml);
Assert.Contains("PowerFlowAutoAccentBrush", appXaml);
Assert.DoesNotContain("FontSize=\"10\"", xaml);
```

- [ ] **Step 2: Run and verify RED**

Expected failure because the current XAML does not have the reference cockpit structure/resources.

- [ ] **Step 3: Add theme resources**

Add deterministic Light/Dark/system-safe resources for canvas, surface, border, cyan accent, semantic state accents, muted secondary text, selected glow, and graph fills. Keep control XAML free of hard-coded semantic colors.

- [ ] **Step 4: Recompose one XAML tree**

Implement:
- compact/expanded branded top header;
- left navigation rail only when profile allows it;
- four semantic mode cards at expanded density and tighter selector at compact density;
- hero trajectory graph centered/left;
- live telemetry/status panel at right using current CPU/package/clock/state information only;
- lower policy/rule/recent-history/quick-action panels only from data/actions already supported;
- Glance density that reuses identity/state/telemetry/trajectory anchors without side navigation/card wall.

- [ ] **Step 5: Apply profile-driven reflow**

In `ApplyShellLayout`, move/re-span shared elements and set visibility/density from the profile. Avoid duplicate graph/state/telemetry controls.

- [ ] **Step 6: Polish trajectory graph without fake data**

Use current telemetry/history/policy series; add grid/soft fills/highlight resources and crisp NOW/hover treatment. Do not add GPU/RAM/temp series unless actual app data already supplies them.

- [ ] **Step 7: Verify GREEN and readability**

Run reference cockpit contract, readability, trajectory, and full App test assembly. Build Release with zero warnings/errors.

- [ ] **Step 8: Commit**

```powershell
git add src/PowerFlow.App tests/PowerFlow.App.Tests
git commit -m "feat: style PowerFlow morphing cockpit"
```

---

### Task 5: Motion sequencing, reverse shrink, reduced motion, and Rules/Settings continuity

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PowerFlowShellLayout.cs`
- Create: `src/PowerFlow.App/Dashboard/ShellMotionPolicy.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/ShellMotionPolicyTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/ProgressivePresentationContractTests.cs`

**Interfaces:**
- `ShellMotionPolicy.Duration(from, to, reducedMotion)` returns bounded timing.
- Growth uses cubic ease-out; collapse uses complementary ease-in/out.
- Shared elements move first; higher-density detail appears after approximately one-third progress.

- [ ] **Step 1: Write failing motion-policy tests**

```csharp
[Fact]
public void Duration_UsesFastBoundedGrowthTimings()
{
    Assert.InRange(ShellMotionPolicy.Duration(Hidden, Glance, false).TotalMilliseconds, 110, 150);
    Assert.InRange(ShellMotionPolicy.Duration(Glance, Compact, false).TotalMilliseconds, 160, 200);
    Assert.InRange(ShellMotionPolicy.Duration(Compact, Expanded, false).TotalMilliseconds, 180, 240);
}

[Fact]
public void Duration_ReducedMotion_IsImmediate()
    => Assert.Equal(TimeSpan.Zero, ShellMotionPolicy.Duration(Glance, Compact, true));
```

- [ ] **Step 2: Run and verify RED**

- [ ] **Step 3: Implement motion policy and transition sequencing**

Integrate Windows `UISettings.AnimationsEnabled` plus `ReducedMotionOverride`. Animate geometry with the policy duration. Apply detail opacity/translation only after progress >= ~0.33 on growth; reverse ordering on shrink. No bounce/elastic easing.

- [ ] **Step 4: Implement shrink-to-tray behavior**

Close/minimize-to-tray from Glance/Compact/Expanded animates toward the last tray anchor before hidden when motion is enabled. Ensure it never terminates the background controller process.

- [ ] **Step 5: Preserve Rules/Settings continuity**

At Compact, Rules/Settings request transitions same shell to Expanded, selects the requested section, and returning Dashboard retains the shared dashboard state/history.

- [ ] **Step 6: Verify GREEN**

Run motion, shell transition, navigation, settings/rules, startup, and presentation tests; Release build.

- [ ] **Step 7: Commit**

```powershell
git add src/PowerFlow.App tests/PowerFlow.App.Tests
git commit -m "feat: choreograph PowerFlow shell motion"
```

---

### Task 6: Remove obsolete tray window and tighten lifecycle/hygiene contracts

**Files:**
- Delete: `src/PowerFlow.App/Tray/TrayHoverWindow.xaml`
- Delete: `src/PowerFlow.App/Tray/TrayHoverWindow.xaml.cs`
- Modify: `src/PowerFlow.App/App.xaml.cs`
- Modify: `tests/PowerFlow.App.Tests/Shell/TrayPopupAndPickerContractTests.cs`
- Create: `tests/PowerFlow.App.Tests/Shell/SingleShellOwnershipContractTests.cs`

**Interfaces:**
- Only `MainWindow` represents a visible PowerFlow shell.
- `App` has one `_shellWindow` owner and no `_trayHoverWindow` field/path.

- [ ] **Step 1: Write failing ownership test before deletion**

```csharp
[Fact]
public void App_OwnsExactlyOneVisualShellPath()
{
    var app = Read("src", "PowerFlow.App", "App.xaml.cs");
    Assert.Contains("MainWindow? _shellWindow", app);
    Assert.DoesNotContain("TrayHoverWindow", app);
    Assert.DoesNotContain("_dashboardWindow", app);
}
```

- [ ] **Step 2: Verify RED while obsolete ownership still exists**

- [ ] **Step 3: Delete tray-window files and remove obsolete preview/capture plumbing tied only to them**

Keep useful tray-anchor projection/policy helpers used by the unified shell; remove only dead visual-window code.

- [ ] **Step 4: Verify GREEN and full App suite**

Run ownership/tray/startup/full App tests and Release build.

- [ ] **Step 5: Commit**

```powershell
git add -A src/PowerFlow.App tests/PowerFlow.App.Tests
git commit -m "refactor: retire separate PowerFlow tray window"
```

---

### Task 7: Rewrite README for the world and recapture truthful current UI

**Files:**
- Rewrite: `README.md`
- Update: `docs/acceptance/reference-host-acceptance.md`
- Replace: `docs/assets/powerflow-popup.png`
- Replace: `docs/assets/powerflow-compressed.png`
- Replace: `docs/assets/powerflow-fullscreen.png` with `docs/assets/powerflow-expanded.png` if Expanded is more useful publicly
- Modify/create README content tests under `tests/PowerFlow.App.Tests/Shell/` if appropriate.

**Interfaces:**
- README has public sections: What it is, Why it exists, How it works, What makes it different, Screenshots, Build/Run, Configuration/Safety, Status, concise AI note.

- [ ] **Step 1: Write failing README contract test**

Assert README:
- contains `What PowerFlow is`/equivalent product introduction, `Why PowerFlow`, `Build` or `Install`, `Configuration`, and `AI`/`vibe-coded` note;
- does **not** contain `reference host`, `HWND`, `179/179`, `CURRENT candidate`, `CRASH_EVENTS`, or capture mechanics;
- AI note is one short paragraph and mentions test-driven/TDD discipline.

- [ ] **Step 2: Run and verify RED against current README**

- [ ] **Step 3: Rewrite README from scratch**

Public-facing tone: useful, witty, concise. Explain adaptive Saver/Balanced/Performance/Auto behavior, hysteresis/holds/cooldown/app/game rules, tray-first low-overhead operation, real Windows plan switching, how to build/run truthfully, config location, safety scope, and short beta limitations. Do not narrate internal qualification history.

Use a concise note such as:

> **AI note:** PowerFlow is vibe-coded with AI, but developed with test-driven discipline: expected behavior is specified in tests before changes are accepted.

- [ ] **Step 4: Capture real shell screenshots after live UI is stable**

Capture Glance, Compact, and Expanded from the same PowerFlow HWND using direct window capture. Validate PID ownership, expected dimensions, and nonblank images. Use no screen-coordinate crop.

- [ ] **Step 5: Update engineering acceptance evidence separately**

Keep detailed test/live evidence in `docs/acceptance/reference-host-acceptance.md`, not README.

- [ ] **Step 6: Verify README contract GREEN**

Run README/source-contract tests and validate all referenced image files/dimensions.

- [ ] **Step 7: Commit**

```powershell
git add README.md docs tests/PowerFlow.App.Tests
git commit -m "docs: present PowerFlow to the world"
```

---

### Task 8: Full regression, live same-HWND acceptance, and cleanup

**Files:**
- No new product files expected.
- Update acceptance evidence only if live results add facts.

**Interfaces:**
- Release gate is the complete solution test/build plus live same-HWND transition evidence and hygiene.

- [ ] **Step 1: Run full automated regression**

```powershell
dotnet test PowerFlow.sln -c Release --no-restore --disable-build-servers --logger "console;verbosity=minimal"
dotnet build PowerFlow.sln -c Release --no-restore --disable-build-servers --nologo
git diff --check
```
Expected: zero failed tests; zero build warnings/errors; clean diff check.

- [ ] **Step 2: Live tray-first hidden-state acceptance**

Start canonical Release with `--background`. Verify one PowerFlow process and no visible shell HWND.

- [ ] **Step 3: Live hover Glance acceptance**

Trigger the real tray-hover path, record PowerFlow HWND, verify ~320x176 and no activation theft.

- [ ] **Step 4: Live single-click pin acceptance**

Single-click tray icon. Verify the **same HWND** is now pinned/active and remains Glance-sized.

- [ ] **Step 5: Live Glance -> Compact acceptance**

Click the Glance surface. Sample window geometry during transition; verify same HWND and final 760x440.

- [ ] **Step 6: Live Compact -> Expanded acceptance**

Click Expand. Verify same HWND, reference-inspired composition, final ~1280x800 clamped to work area, and fluid manual resizing at multiple intermediate widths.

- [ ] **Step 7: Reverse transition acceptance**

Collapse to Compact and hide/shrink to tray. Verify same HWND until hidden and no second visual PowerFlow window appears.

- [ ] **Step 8: Rules/Settings acceptance**

Open both through the same shell, verify automatic growth when necessary, then return to Dashboard with telemetry/history retained.

- [ ] **Step 9: Crash and process hygiene**

Check Application/.NET/WER events since acceptance start: zero new PowerFlow crashes. Verify exactly one process, canonical Release executable, canonical startup registration, one worktree after final integration cleanup, no stale feature process or obsolete screenshots.

- [ ] **Step 10: Final screenshot/public-doc verification**

Verify README references only current Glance/Compact/Expanded assets and reads as product documentation. Re-run any README contract test after final capture changes.

- [ ] **Step 11: Final verification commit only if acceptance docs changed**

Commit evidence separately from product code.

---

## Self-Review

- Spec coverage: every approved shell state, tray interaction, geometry/motion rule, one-window ownership rule, visual-system requirement, README rule, and live acceptance criterion maps to a task above.
- Placeholder scan: no TBD/TODO/"implement later" steps are present.
- Type consistency: shell state/activation/layout/geometry interfaces are introduced in Task 1 and reused consistently by Tasks 3-8.
- Scope check: no policy rewrite, new telemetry backend, installer, or unrelated diagnostics are included.
