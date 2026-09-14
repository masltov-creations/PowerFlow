# PowerFlow Reference Cockpit IA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild PowerFlow so Expanded closely follows the supplied cockpit reference, then derive Compact and Glance as progressively compressed presentations of the same five operational anchors without losing state, live condition, trajectory, controlling cause, or next action.

**Architecture:** Replace the current visibility-boolean shell profile with semantic presentation states. Move the persistent dashboard regions into density-aware controls owned by the existing single `MainWindow`, build the Expanded cockpit first, then reuse those same controls for Compact and Glance. Preserve the existing one-HWND tray-origin geometry, policy semantics, telemetry cadence, and controller behavior; add only cheap truthful system-memory and machine-identity telemetry.

**Tech Stack:** .NET 8, C# 12, WinUI 3, Windows App SDK 2.4.0, x64, native Win32/PDH/powrprof/kernel32 telemetry, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-09-powerflow-morphing-shell-design.md`

## Global Constraints
- **reference host UI Safety — HARD RULE:** No visible PowerFlow UI, tray interaction, UI Automation, focus/activation, synthetic input, preview mode, or other desktop-surfacing action may run on reference host without fresh explicit user authorization for that specific live-UI action. Default execution is headless only. If a task requires live UI and approval is absent, stop at the headless gate.

- The supplied dashboard image is the visual and information-architecture reference.
- Expanded is implemented first; Compact and Glance are derived from the correct Expanded architecture.
- One visual PowerFlow HWND only. Do not reintroduce `TrayHoverWindow` or any second visual shell.
- Preserve `PowerFlowShellState`: `Hidden`, `Glance`, `Compact`, `Expanded`, `FullScreen`.
- Canonical resting sizes remain Glance **320 x 176**, Compact **760 x 440**, Expanded **1280 x 800**; Full Screen uses the work area/full-screen presenter.
- At every visible density the UI must answer: current state/control, live machine condition, recent trajectory, controlling cause/lock/rule, and next likely action.
- Compact must not hide live stats or control context.
- Glance must retain all five essentials without exposing navigation, Rules forms, Settings forms, or a card wall.
- Expanded uses a persistent left rail with real destinations only: Dashboard, Rules / Apps, Settings. Do not add dead destinations to imitate the reference.
- Manual/game latch is a control-state overlay, not a fifth power plan.
- Power Saver = green, Balanced = blue/cyan, Performance = orange, Auto = purple.
- Existing real telemetry remains CPU utilization, package watts, average CPU frequency, state/latch/progress/cooldown/trigger/reason/history/rules.
- Add only cheap truthful memory usage and basic machine identity. GPU telemetry is optional and out of the critical path. Do not fabricate GPU, temperature, RAM, device, or per-process-watt values.
- Do not label a panel `Top Power Consumers` unless per-process watts are actually measured. Use truthful `Active Sources` data instead.
- Explicit visible text must remain at least 11 px; no scroll viewer on the primary dashboard resting states.
- Window/interior motion stays bounded to roughly **120-240 ms**. Reduced Motion preserves the same information state with interpolation suppressed.
- No controller-policy semantic changes, BIOS/hardware tuning, packaging/signing, installer work, or unrelated Rules/Settings redesign.
- The existing uncommitted README rewrite is stale relative to this experiment. At implementation start restore `README.md` to `HEAD` and remove the untracked `tests/PowerFlow.App.Tests/Shell/ReadmeContractTests.cs`. README work occurs only after the user visually accepts the cockpit.
- Every product-code task follows RED -> GREEN -> focused regression -> commit.
- Before declaring readiness: Core, Windows, and App suites pass on the exact candidate; Release build has zero errors; live same-HWND acceptance covers all four densities; the user visually approves the actual UI.

---

### Task 1: Replace Visibility Booleans with a Semantic Shell Presentation Model

**Files:**
- Create: `src/PowerFlow.App/Dashboard/ShellPresentationProfile.cs`
- Modify: `src/PowerFlow.App/Dashboard/PowerFlowShellLayout.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/PowerFlowShellLayoutTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/ShellPresentationProfileTests.cs`

**Interfaces:**
- Consumes: `PowerFlowShellState` from `PowerFlowShellState.cs`.
- Produces: `ShellPresentationProfile PowerFlowShellLayout.Resolve(int width, int height, PowerFlowShellState requestedState, string section)` for every later UI task.

- [ ] **Step 1: Remove stale README experiment from the working tree before product work**

Run:

```powershell
git restore -- README.md
if (Test-Path tests\PowerFlow.App.Tests\Shell\ReadmeContractTests.cs) {
    Remove-Item tests\PowerFlow.App.Tests\Shell\ReadmeContractTests.cs
}
git status --short
```

Expected: only intentional implementation work appears after this point; README is clean and the premature README contract is absent.

- [ ] **Step 2: Write failing semantic-profile tests**

Add tests equivalent to:

```csharp
[Theory]
[InlineData(PowerFlowShellState.Glance,
    NavigationPresentation.None, HeaderPresentation.Minimal,
    ModePresentation.CurrentChip, StatsPresentation.Inline,
    TrajectoryPresentation.Minimal, ControlContextPresentation.CauseLine,
    SecondaryPresentation.Hidden)]
[InlineData(PowerFlowShellState.Compact,
    NavigationPresentation.Overlay, HeaderPresentation.Compact,
    ModePresentation.Segmented, StatsPresentation.CompactRail,
    TrajectoryPresentation.Compact, ControlContextPresentation.Rail,
    SecondaryPresentation.Hidden)]
[InlineData(PowerFlowShellState.Expanded,
    NavigationPresentation.Rail, HeaderPresentation.System,
    ModePresentation.Cards, StatsPresentation.FullRail,
    TrajectoryPresentation.Full, ControlContextPresentation.Modules,
    SecondaryPresentation.Full)]
[InlineData(PowerFlowShellState.FullScreen,
    NavigationPresentation.Rail, HeaderPresentation.System,
    ModePresentation.Cards, StatsPresentation.FullRail,
    TrajectoryPresentation.Full, ControlContextPresentation.Modules,
    SecondaryPresentation.Full)]
public void Resolve_MapsStateToSemanticPresentations(
    PowerFlowShellState state,
    NavigationPresentation navigation,
    HeaderPresentation header,
    ModePresentation modes,
    StatsPresentation stats,
    TrajectoryPresentation trajectory,
    ControlContextPresentation context,
    SecondaryPresentation secondary)
{
    var profile = PowerFlowShellLayout.Resolve(
        state == PowerFlowShellState.Glance ? 320 : state == PowerFlowShellState.Compact ? 760 : 1280,
        state == PowerFlowShellState.Glance ? 176 : state == PowerFlowShellState.Compact ? 440 : 800,
        state,
        "flow");

    Assert.Equal(navigation, profile.Navigation);
    Assert.Equal(header, profile.Header);
    Assert.Equal(modes, profile.Modes);
    Assert.Equal(stats, profile.Stats);
    Assert.Equal(trajectory, profile.Trajectory);
    Assert.Equal(context, profile.ControlContext);
    Assert.Equal(secondary, profile.Secondary);
}

[Fact]
public void Compact_PreservesStatsAndControlContext()
{
    var profile = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "flow");
    Assert.Equal(StatsPresentation.CompactRail, profile.Stats);
    Assert.Equal(ControlContextPresentation.Rail, profile.ControlContext);
}

[Fact]
public void Expanded_UsesReferenceAsymmetricPrimarySplit()
{
    var profile = PowerFlowShellLayout.Resolve(1280, 800, PowerFlowShellState.Expanded, "flow");
    Assert.InRange(profile.Geometry.PrimaryGraphFraction, 0.68, 0.72);
    Assert.True(profile.Geometry.NavigationWidth >= 126);
    Assert.True(profile.Geometry.ControlBandHeight > 0);
    Assert.True(profile.Geometry.SecondaryBandHeight > 0);
}
```

- [ ] **Step 3: Run the new tests to prove RED**

Run:

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PowerFlowShellLayoutTests|FullyQualifiedName~ShellPresentationProfileTests"
```

Expected: compile/test failure because the semantic presentation enums/profile do not exist yet and `PowerFlowShellLayout.Resolve` still returns the old boolean-heavy profile.

- [ ] **Step 4: Add the semantic profile types**

Create `ShellPresentationProfile.cs` with these exact public shapes:

```csharp
namespace PowerFlow.App.Dashboard;

public enum NavigationPresentation { None, Overlay, Rail }
public enum HeaderPresentation { Minimal, Compact, System }
public enum ModePresentation { CurrentChip, Segmented, Cards }
public enum StatsPresentation { Inline, CompactRail, FullRail }
public enum TrajectoryPresentation { Minimal, Compact, Full }
public enum ControlContextPresentation { CauseLine, Rail, Modules }
public enum SecondaryPresentation { Hidden, Summary, Full }

public sealed record ShellGeometry(
    double NavigationWidth,
    double ContentPadding,
    double Gap,
    double HeaderHeight,
    double ModeBandHeight,
    double PrimaryGraphFraction,
    double ControlBandHeight,
    double SecondaryBandHeight);

public sealed record ShellPresentationProfile(
    PowerFlowShellState State,
    NavigationPresentation Navigation,
    HeaderPresentation Header,
    ModePresentation Modes,
    StatsPresentation Stats,
    TrajectoryPresentation Trajectory,
    ControlContextPresentation ControlContext,
    SecondaryPresentation Secondary,
    ShellGeometry Geometry);
```

- [ ] **Step 5: Rewrite `PowerFlowShellLayout.Resolve` to return semantic presentations**

Use canonical mappings, keeping geometry fluid inside Expanded/Full rather than toggling more booleans:

```csharp
public static ShellPresentationProfile Resolve(
    int width,
    int height,
    PowerFlowShellState requestedState,
    string section)
{
    width = Math.Max(1, width);
    height = Math.Max(1, height);
    section = string.IsNullOrWhiteSpace(section) ? "flow" : section;

    var state = requestedState;
    if (!string.Equals(section, "flow", StringComparison.OrdinalIgnoreCase) && state == PowerFlowShellState.Compact)
        state = PowerFlowShellState.Expanded;

    return state switch
    {
        PowerFlowShellState.Glance => new(
            state, NavigationPresentation.None, HeaderPresentation.Minimal,
            ModePresentation.CurrentChip, StatsPresentation.Inline,
            TrajectoryPresentation.Minimal, ControlContextPresentation.CauseLine,
            SecondaryPresentation.Hidden,
            new ShellGeometry(0, 8, 6, 28, 30, 0.72, 24, 0)),

        PowerFlowShellState.Compact => new(
            state, NavigationPresentation.Overlay, HeaderPresentation.Compact,
            ModePresentation.Segmented, StatsPresentation.CompactRail,
            TrajectoryPresentation.Compact, ControlContextPresentation.Rail,
            SecondaryPresentation.Hidden,
            new ShellGeometry(0, 10, 8, 42, 48, 0.68, 62, 0)),

        PowerFlowShellState.FullScreen => Expanded(width, height, state, true),
        PowerFlowShellState.Expanded => Expanded(width, height, state, false),
        _ => new(
            PowerFlowShellState.Hidden, NavigationPresentation.None, HeaderPresentation.Minimal,
            ModePresentation.CurrentChip, StatsPresentation.Inline,
            TrajectoryPresentation.Minimal, ControlContextPresentation.CauseLine,
            SecondaryPresentation.Hidden,
            new ShellGeometry(0, 0, 0, 0, 0, 0.70, 0, 0))
    };
}
```

`Expanded(...)` must produce `Rail/System/Cards/FullRail/Full/Modules/Full` with `PrimaryGraphFraction` clamped to `0.68..0.72`, navigation width roughly `128..152`, and padding/gaps increasing continuously with available space.

- [ ] **Step 6: Run focused GREEN and the existing shell-layout regression ring**

Run:

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PowerFlowShellLayout|FullyQualifiedName~DashboardResponsiveLayout|FullyQualifiedName~DashboardCompactness"
```

Expected: all selected tests pass after updating stale assertions to the semantic model; no test may reintroduce `ShowLiveStatsPanel`/`ShowLowerContextPanels` as the design contract.

- [ ] **Step 7: Commit Task 1**

```powershell
git add src\PowerFlow.App\Dashboard\ShellPresentationProfile.cs src\PowerFlow.App\Dashboard\PowerFlowShellLayout.cs tests\PowerFlow.App.Tests\Dashboard
git commit -m "refactor: model PowerFlow shell by semantic density"
```

---

### Task 2: Componentize Header, Power Modes, and the Existing Trajectory Anchor

**Files:**
- Create: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml.cs`
- Create: `src/PowerFlow.App/Dashboard/PowerModeControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/PowerModeControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/TrajectoryControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/PrimaryAnchorComponentTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/TrajectoryDashboardContractTests.cs`

**Interfaces:**
- Consumes: `HeaderPresentation`, `ModePresentation`, `TrajectoryPresentation`, `DashboardViewModel`.
- Produces: density-aware `ShellHeaderControl`, `PowerModeControl`, and `TrajectoryControl.SetShellPresentation(...)` consumed by `MainWindow`.

- [ ] **Step 1: Add RED component contracts**

Tests must assert one reusable component per lineage and no density-specific duplicate mode selector:

```csharp
[Fact]
public void PowerModeControl_ExposesOneDensityAwareControlSurface()
{
    var code = Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml.cs");
    var xaml = Read("src", "PowerFlow.App", "Dashboard", "PowerModeControl.xaml");
    Assert.Contains("ModePresentation Presentation", code);
    Assert.Contains("ModeRequested", code);
    Assert.Contains("CurrentChipLayer", xaml);
    Assert.Contains("SegmentedLayer", xaml);
    Assert.Contains("CardsLayer", xaml);
}

[Fact]
public void TrajectoryControl_AcceptsSemanticPresentation()
{
    var code = Read("src", "PowerFlow.App", "Dashboard", "TrajectoryControl.xaml.cs");
    Assert.Contains("SetShellPresentation(TrajectoryPresentation", code);
}
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PrimaryAnchorComponentTests|FullyQualifiedName~TrajectoryDashboardContractTests"
```

Expected: fail because the new controls/interfaces do not exist.

- [ ] **Step 3: Add view-model selection/control properties without changing policy behavior**

Add properties to `DashboardViewModel` derived only from `ControllerSnapshot`:

```csharp
public bool IsPowerSaverSelected => _snapshot?.State == PowerState.PowerSaver;
public bool IsBalancedSelected => _snapshot?.State == PowerState.Balanced;
public bool IsPerformanceSelected => _snapshot?.State == PowerState.HighPerformance;
public bool IsAutoSelected => _snapshot is null ||
    !(_snapshot.IsLatched && string.Equals(_snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase));

public string ControlBadgeLabel => _snapshot switch
{
    { IsLatched: true, LatchType: "Manual" } => "MANUAL LOCK",
    { IsLatched: true, LatchType: "Game" } => "GAME LATCH",
    _ => "AUTO"
};
```

Raise property-changed notifications for these properties whenever the snapshot updates.

- [ ] **Step 4: Implement `ShellHeaderControl`**

Expose a dependency property:

```csharp
public HeaderPresentation Presentation
{
    get => (HeaderPresentation)GetValue(PresentationProperty);
    set => SetValue(PresentationProperty, value);
}

public static readonly DependencyProperty PresentationProperty =
    DependencyProperty.Register(nameof(Presentation), typeof(HeaderPresentation),
        typeof(ShellHeaderControl), new PropertyMetadata(HeaderPresentation.Compact, OnPresentationChanged));
```

The XAML must contain named `MinimalHeader`, `CompactHeader`, and `SystemHeader` layers inside one control. Bind identity/state/status to the inherited `DashboardViewModel`; do not add a promotional brand card.

- [ ] **Step 5: Implement `PowerModeControl` with one event contract**

Use:

```csharp
public enum PowerModeChoice { PowerSaver, Balanced, Performance, Auto }

public sealed class PowerModeRequestedEventArgs(PowerModeChoice choice) : EventArgs
{
    public PowerModeChoice Choice { get; } = choice;
}

public event EventHandler<PowerModeRequestedEventArgs>? ModeRequested;
public ModePresentation Presentation { get; set; } // dependency property in implementation
```

The XAML contains three presentation layers in one control:

```xml
<Grid>
    <Grid x:Name="CurrentChipLayer" Visibility="Collapsed" />
    <Grid x:Name="SegmentedLayer" Visibility="Collapsed" />
    <Grid x:Name="CardsLayer" Visibility="Collapsed" />
</Grid>
```

Populate each layer with the same four semantic choices where applicable. Expanded cards use icon/name/description plus unmistakable selected surface/glow/type treatment. Compact uses four segments. Glance displays the current state capsule plus `ControlBadgeLabel` rather than four tiny buttons.

- [ ] **Step 6: Preserve existing mode semantics in `MainWindow` through one dispatcher**

Replace four separate direct button handlers with:

```csharp
private async void OnPowerModeRequested(object? sender, PowerModeRequestedEventArgs e)
{
    switch (e.Choice)
    {
        case PowerModeChoice.Auto:
            if (string.Equals(_controller.Snapshot.LatchType, "Manual", StringComparison.OrdinalIgnoreCase))
                await _controller.ReleaseManualLatchAsync();
            ApplyVisualState(_controller.Snapshot);
            break;
        case PowerModeChoice.PowerSaver:
            await _controller.SetManualStateAsync(PowerState.PowerSaver);
            break;
        case PowerModeChoice.Balanced:
            await _controller.SetManualStateAsync(PowerState.Balanced);
            break;
        case PowerModeChoice.Performance:
            await _controller.SetManualStateAsync(PowerState.HighPerformance);
            break;
    }
}
```

Do not change `PowerFlowController` or `PowerPolicyEngine` semantics.

- [ ] **Step 7: Add semantic trajectory presentation adapter**

Add:

```csharp
public void SetShellPresentation(TrajectoryPresentation presentation, double targetHeight)
{
    var profile = presentation switch
    {
        TrajectoryPresentation.Minimal => new DashboardLayoutProfile(
            DashboardPresentationMode.Compressed, true, false, false, false,
            16, 13, 6, 4, 6, 160, Math.Max(48, targetHeight), 3, 180, 8),
        TrajectoryPresentation.Compact => DashboardResponsiveLayout.Resolve(760, 440, false),
        _ => DashboardResponsiveLayout.Resolve(1280, 800, false)
    };
    SetLayoutProfile(profile);
}
```

Then refine visibility inside `TrajectoryControl` so Minimal hides axes/range chrome, Compact keeps NOW/state bands/major thresholds, and Full retains legend/range/transitions/hover detail.

- [ ] **Step 8: Run focused GREEN plus controller-behavior contracts**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PrimaryAnchorComponentTests|FullyQualifiedName~Trajectory|FullyQualifiedName~ManualLockPresentation|FullyQualifiedName~PowerFlowControllerTests"
```

Expected: pass; mode requests still call the same controller methods.

- [ ] **Step 9: Commit Task 2**

```powershell
git add src\PowerFlow.App\Dashboard tests\PowerFlow.App.Tests\Dashboard
git commit -m "refactor: componentize PowerFlow primary anchors"
```

---

### Task 3: Componentize Live Stats, Control Context, and Operational Context

**Files:**
- Create: `src/PowerFlow.App/Dashboard/LiveStatsControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/LiveStatsControl.xaml.cs`
- Create: `src/PowerFlow.App/Dashboard/ControlContextControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/ControlContextControl.xaml.cs`
- Create: `src/PowerFlow.App/Dashboard/OperationalContextControl.xaml`
- Create: `src/PowerFlow.App/Dashboard/OperationalContextControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/OperationalProjectionTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/SecondaryAnchorComponentTests.cs`

**Interfaces:**
- Consumes: one `DashboardViewModel`, `StatsPresentation`, `ControlContextPresentation`, `SecondaryPresentation`.
- Produces: three reusable controls that never infer window size themselves.

- [ ] **Step 1: Write RED projection tests for the three distinct control-context questions**

```csharp
[Fact]
public void ManualLatch_ProjectsOwnerCauseAndNextAsDistinctAnswers()
{
    var vm = NewViewModel(new ControllerSnapshot(
        PowerState.Balanced, "Pinned for analysis", true, "Manual", 22, .4,
        null, "tool.exe", Now, [], 1, 1));

    Assert.Equal("Manual lock", vm.ControlOwnerLabel);
    Assert.Contains("tool.exe", vm.CauseLabel);
    Assert.Contains("manual", vm.ControlDetailLabel, StringComparison.OrdinalIgnoreCase);
    Assert.NotEqual(vm.ControlOwnerLabel, vm.NextActionLabel);
}

[Fact]
public void ActiveSources_NeverClaimPerProcessWatts()
{
    var vm = NewViewModel(/* snapshot with trigger app */);
    Assert.All(vm.ActiveSourceLines, line => Assert.DoesNotContain(" W", line));
}
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~OperationalProjectionTests|FullyQualifiedName~SecondaryAnchorComponentTests"
```

Expected: fail because the new view-model properties and controls do not exist.

- [ ] **Step 3: Add truthful operational projections to `DashboardViewModel`**

Add:

```csharp
public string ControlOwnerLabel => _snapshot switch
{
    { IsLatched: true, LatchType: "Manual" } => "Manual lock",
    { IsLatched: true, LatchType: "Game" } => "Game latch",
    _ => "Auto"
};

public string ControlDetailLabel => _snapshot switch
{
    { IsLatched: true, LatchType: "Manual" } => $"Locked to {StateLabel.Replace(" LOCKED", "", StringComparison.Ordinal)}",
    { IsLatched: true, LatchType: "Game", TriggerApplication: { Length: > 0 } app } => $"Held by {app}",
    { IsLatched: true, LatchType: "Game" } => "Held until the tracked game exits",
    _ => "Rules and sustained load choose the state"
};

public string CauseLabel => string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication)
    ? Reason
    : $"{_snapshot!.TriggerApplication} - {Reason}";

public string CooldownLabel => _snapshot?.CooldownRemaining is { } remaining && remaining > TimeSpan.Zero
    ? $"Cooldown {Math.Ceiling(remaining.TotalSeconds):0}s"
    : "No cooldown";

public string PolicyProgressLabel => _snapshot is null
    ? "Establishing policy"
    : $"{Math.Clamp(_snapshot.ThresholdProgress, 0, 1) * 100:0}% toward decision";

public IReadOnlyList<string> ActiveSourceLines
{
    get
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(_snapshot?.TriggerApplication))
            lines.Add($"Trigger: {_snapshot!.TriggerApplication}");
        foreach (var rule in RuleCards.Where(x => x.Status is "ACTIVE" or "LOCKED").Take(3))
            lines.Add($"{rule.Title}: {rule.Status}");
        if (lines.Count == 0) lines.Add("No explicit active source");
        return lines;
    }
}
```

Raise notifications for all added properties on snapshot/config update.

- [ ] **Step 4: Implement `LiveStatsControl`**

Expose `StatsPresentation Presentation` as a dependency property. One control contains `InlineStats`, `CompactStatsRail`, and `FullStatsRail` layers. Bind only real labels: CPU, PACKAGE, AVG CLOCK, and later MEMORY when available. Before Task 7, MEMORY renders `-`/`Unavailable`, never a made-up value.

The Full layer uses a first-class gauge/numeric treatment, for example:

```xml
<Grid x:Name="FullStatsRail" RowSpacing="10">
    <TextBlock Text="LIVE STATS" FontSize="13" FontWeight="SemiBold" />
    <Border Style="{StaticResource PowerFlowInstrumentCardStyle}">
        <Grid ColumnSpacing="10">
            <TextBlock Text="CPU" FontSize="11" />
            <ProgressBar Grid.Column="1" Maximum="100" Value="{Binding CpuPercent}" Height="6" />
            <TextBlock Grid.Column="2" Text="{Binding CpuLabel}" FontSize="18" FontWeight="SemiBold" />
        </Grid>
    </Border>
</Grid>
```

Add a numeric `CpuPercent` property to `DashboardViewModel` rather than parsing `CpuLabel` in XAML.

- [ ] **Step 5: Implement `ControlContextControl`**

Expose `ControlContextPresentation Presentation`. One control contains:

- `CauseLineLayer` for Glance;
- `ContextRailLayer` for Compact;
- `ContextModulesLayer` for Expanded/Full.

The modules are named `ControlLockModule`, `ActiveCauseModule`, and `PowerFlowNextModule` and answer distinct questions. `ControlLockModule` may expose `ReleaseManualRequested` only when a manual latch exists; it must not imply a game latch can be manually released if policy does not allow that.

- [ ] **Step 6: Implement `OperationalContextControl`**

Expose `SecondaryPresentation Presentation` and events:

```csharp
public event EventHandler? RulesRequested;
public event EventHandler? SettingsRequested;
```

Full presentation contains named `RecentEventsRegion`, `ActiveSourcesRegion`, and `QuickActionsRegion`. The middle region is labeled **ACTIVE SOURCES**, not Top Power Consumers. Bind `RecentEventLines` and `ActiveSourceLines`.

- [ ] **Step 7: Run focused GREEN and readability contracts**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~OperationalProjectionTests|FullyQualifiedName~SecondaryAnchorComponentTests|FullyQualifiedName~ReadabilityLayoutContractTests"
```

Expected: pass; no explicit text below 11 px.

- [ ] **Step 8: Commit Task 3**

```powershell
git add src\PowerFlow.App\Dashboard tests\PowerFlow.App.Tests\Dashboard
git commit -m "feat: add density-aware PowerFlow cockpit regions"
```

---

### Task 4: Rebuild Expanded First to Match the Reference Cockpit Hierarchy

**Files:**
- Modify: `src/PowerFlow.App/App.xaml`
- Replace dashboard composition in: `src/PowerFlow.App/Dashboard/MainWindow.xaml`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/ReferenceCockpitContractTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/DashboardInformationArchitectureTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/ExpandedCockpitLayoutTests.cs`

**Interfaces:**
- Consumes all density-aware controls from Tasks 2-3 and `ShellPresentationProfile` from Task 1.
- Produces the canonical Expanded source architecture from which Compact and Glance will be compressed.

- [ ] **Step 1: Write RED source/layout contracts for the reference hierarchy**

Assert the Expanded composition contains the actual named regions and asymmetric grid:

```csharp
[Fact]
public void Expanded_HasReferenceCockpitRegionsInOperationalOrder()
{
    var xaml = ReadMainWindow();
    Assert.Contains("x:Name=\"SystemHeaderHost\"", xaml);
    Assert.Contains("x:Name=\"PowerModeBandHost\"", xaml);
    Assert.Contains("x:Name=\"PrimaryAnalyticalRow\"", xaml);
    Assert.Contains("x:Name=\"ControlContextBandHost\"", xaml);
    Assert.Contains("x:Name=\"SecondaryOperationalRow\"", xaml);
    Assert.Contains("x:Name=\"StatusFooter\"", xaml);
    Assert.Contains("<ColumnDefinition Width=\"7*\"", xaml);
    Assert.Contains("<ColumnDefinition Width=\"3*\"", xaml);
}

[Fact]
public void Expanded_UsesOneInstanceOfEachMorphingAnchor()
{
    var xaml = ReadMainWindow();
    Assert.Equal(1, Count(xaml, "<dash:PowerModeControl"));
    Assert.Equal(1, Count(xaml, "<dash:LiveStatsControl"));
    Assert.Equal(1, Count(xaml, "<dash:TrajectoryControl"));
    Assert.Equal(1, Count(xaml, "<dash:ControlContextControl"));
}
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ReferenceCockpitContractTests|FullyQualifiedName~DashboardInformationArchitectureTests|FullyQualifiedName~ExpandedCockpitLayoutTests"
```

Expected: fail against the current brand-card + generic right panel + equal lower cards.

- [ ] **Step 3: Strengthen the reference visual language in `App.xaml`**

Keep existing public brush names where possible and add reusable cockpit resources, using a restrained dark palette:

```xml
<SolidColorBrush x:Key="PowerFlowCanvasBrush" Color="#071019" />
<SolidColorBrush x:Key="PowerFlowSurfaceBrush" Color="#0C1824" />
<SolidColorBrush x:Key="PowerFlowSurfaceElevatedBrush" Color="#112331" />
<SolidColorBrush x:Key="PowerFlowPanelBrush" Color="#0A1520" />
<SolidColorBrush x:Key="PowerFlowCardBorderBrush" Color="#244151" />
<SolidColorBrush x:Key="PowerFlowAccentBrush" Color="#55E6F2" />
<SolidColorBrush x:Key="PowerFlowSaverAccentBrush" Color="#57D77A" />
<SolidColorBrush x:Key="PowerFlowBalancedAccentBrush" Color="#4EDCFF" />
<SolidColorBrush x:Key="PowerFlowPerformanceAccentBrush" Color="#FFB24C" />
<SolidColorBrush x:Key="PowerFlowAutoAccentBrush" Color="#B58BFF" />
```

Add `PowerFlowInstrumentCardStyle` for rounded 12-15 px surfaces with subtle border and no huge drop-shadow cost. Selected mode surfaces use semantic accent wash + icon/type emphasis, not border color alone.

- [ ] **Step 4: Replace `MainWindow` dashboard composition with the reference grid**

The Expanded dashboard tree must have this structure using one instance of each component:

```xml
<Grid x:Name="CockpitRoot">
    <Grid.ColumnDefinitions>
        <ColumnDefinition x:Name="NavigationColumn" Width="144" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>

    <NavigationView x:Name="NavigationRail" Grid.Column="0" />

    <Grid x:Name="CockpitSurface" Grid.Column="1" RowSpacing="12" Padding="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <dash:ShellHeaderControl x:Name="SystemHeaderHost" />
        <dash:PowerModeControl x:Name="PowerModeBandHost" Grid.Row="1" ModeRequested="OnPowerModeRequested" />

        <Grid x:Name="PrimaryAnalyticalRow" Grid.Row="2" ColumnSpacing="12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="7*" />
                <ColumnDefinition Width="3*" />
            </Grid.ColumnDefinitions>
            <dash:TrajectoryControl x:Name="Trajectory" />
            <dash:LiveStatsControl x:Name="LiveStatsHost" Grid.Column="1" />
        </Grid>

        <dash:ControlContextControl x:Name="ControlContextBandHost" Grid.Row="3" />
        <dash:OperationalContextControl x:Name="SecondaryOperationalRow" Grid.Row="4"
            RulesRequested="OnOperationalRulesRequested"
            SettingsRequested="OnOperationalSettingsRequested" />
        <Grid x:Name="StatusFooter" Grid.Row="5" />
    </Grid>
</Grid>
```

Use native navigation only for Dashboard, Rules / Apps, Settings. Brand/system context belongs in the rail/header rather than a standalone promotional card.

- [ ] **Step 5: Make `ApplyShellLayout` a pure presentation dispatcher, not a visibility wall**

Replace old statements such as `LiveStatsPanel.Visibility = ...` with:

```csharp
private void ApplyShellLayout(PowerFlowShellState state, int width, int height)
{
    var profile = PowerFlowShellLayout.Resolve(width, height, state, _currentSection);
    SystemHeaderHost.Presentation = profile.Header;
    PowerModeBandHost.Presentation = profile.Modes;
    LiveStatsHost.Presentation = profile.Stats;
    ControlContextBandHost.Presentation = profile.ControlContext;
    SecondaryOperationalRow.Presentation = profile.Secondary;
    Trajectory.SetShellPresentation(profile.Trajectory, ResolveTrajectoryHeight(profile, height));
    ApplyNavigationPresentation(profile.Navigation, profile.Geometry.NavigationWidth);
    ApplyCockpitGeometry(profile);
}
```

At Expanded 1280x800, `ApplyCockpitGeometry` leaves the 7:3 primary split, full three-part control band, full secondary row, slim footer, and left rail visible.

- [ ] **Step 6: Wire actions without changing existing controller/navigation semantics**

`OperationalContextControl.RulesRequested` calls the existing `SelectSection("rules")` + Expanded transition path. Settings does the same for `settings`. `ControlContextControl.ReleaseManualRequested` calls `_controller.ReleaseManualLatchAsync()` only for a current Manual latch.

- [ ] **Step 7: Run Expanded GREEN, Release build, and visual-contract ring**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ReferenceCockpit|FullyQualifiedName~DashboardInformationArchitecture|FullyQualifiedName~ExpandedCockpit|FullyQualifiedName~Readability|FullyQualifiedName~SafeTheme"
dotnet build src\PowerFlow.App\PowerFlow.App.csproj -c Release --no-restore
```

Expected: tests pass; build has zero errors; XAML compiler accepts the new cockpit.

- [ ] **Step 8: Commit Task 4**

```powershell
git add src\PowerFlow.App tests\PowerFlow.App.Tests\Dashboard
git commit -m "feat: rebuild PowerFlow expanded reference cockpit"
```

---

### Task 5: Derive Compact from the Same Cockpit Components

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: component XAML/code from Tasks 2-3 only as needed for Compact presentation
- Create: `tests/PowerFlow.App.Tests/Dashboard/CompactCockpitInformationTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Shell/CompactShellContractTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/DashboardCompactnessTests.cs`

**Interfaces:**
- Consumes the same component instances built for Expanded.
- Produces the 760x440 daily cockpit with all five essential answers.

- [ ] **Step 1: Write RED tests that forbid information loss in Compact**

```csharp
[Fact]
public void Compact_KeepsAllFiveEssentialAnchors()
{
    var profile = PowerFlowShellLayout.Resolve(760, 440, PowerFlowShellState.Compact, "flow");
    Assert.Equal(ModePresentation.Segmented, profile.Modes);
    Assert.Equal(StatsPresentation.CompactRail, profile.Stats);
    Assert.Equal(TrajectoryPresentation.Compact, profile.Trajectory);
    Assert.Equal(ControlContextPresentation.Rail, profile.ControlContext);
}

[Fact]
public void MainWindow_DoesNotCollapseStatsOrControlContextForCompact()
{
    var code = ReadMainWindowCode();
    Assert.DoesNotContain("LiveStatsHost.Visibility = Visibility.Collapsed", code);
    Assert.DoesNotContain("ControlContextBandHost.Visibility = Visibility.Collapsed", code);
}
```

- [ ] **Step 2: Run RED against the Expanded-only composition behavior**

Run the Compact test classes; expected failure until placement/presentation is implemented.

- [ ] **Step 3: Implement Compact placement using the same instances**

At Compact:

- no permanent left rail; navigation uses a small overflow affordance in `ShellHeaderControl`;
- header = one compact row;
- modes = segmented selector;
- primary row = trajectory approximately two-thirds and compact stats rail approximately one-third;
- control context = one horizontal rail below;
- secondary operational row = hidden because its essential content has already compressed into control context and header actions.

Do not create `CompactLiveStatsControl`, `CompactModeSelector`, or another `TrajectoryControl`.

- [ ] **Step 4: Make 760x440 no-scroll/readability explicit**

Use geometry from the profile to fit the rest state. If space is tight, reduce decoration/padding and secondary labels before reducing font size. All explicit visible text remains >=11 px.

- [ ] **Step 5: Run Compact GREEN and full dashboard layout ring**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CompactCockpit|FullyQualifiedName~CompactShell|FullyQualifiedName~DashboardCompactness|FullyQualifiedName~ReadabilityLayout"
```

- [ ] **Step 6: Commit Task 5**

```powershell
git add src\PowerFlow.App\Dashboard tests\PowerFlow.App.Tests
git commit -m "feat: derive compact cockpit from PowerFlow reference layout"
```

---

### Task 6: Derive Glance from the Same Cockpit Components

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: density-aware controls from Tasks 2-3 only for Glance presentation
- Create: `tests/PowerFlow.App.Tests/Dashboard/GlanceInformationArchitectureTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Shell/TrayPopupAndPickerContractTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Tray/TrayTrajectoryVisualContractTests.cs`

**Interfaces:**
- Consumes the same header/mode/stats/trajectory/control-context instances.
- Produces a 320x176 crop of the same cockpit anchored to the real tray geometry.

- [ ] **Step 1: Write RED Glance essentials test**

```csharp
[Fact]
public void Glance_ContainsStateStatsTrajectoryCauseAndNextWithoutNavigation()
{
    var profile = PowerFlowShellLayout.Resolve(320, 176, PowerFlowShellState.Glance, "flow");
    Assert.Equal(NavigationPresentation.None, profile.Navigation);
    Assert.Equal(ModePresentation.CurrentChip, profile.Modes);
    Assert.Equal(StatsPresentation.Inline, profile.Stats);
    Assert.Equal(TrajectoryPresentation.Minimal, profile.Trajectory);
    Assert.Equal(ControlContextPresentation.CauseLine, profile.ControlContext);
}
```

Also assert MainWindow contains only one instance of each anchor and Glance still uses `ShellTransitionGeometry`/real tray anchor.

- [ ] **Step 2: Run RED**

Expected: failure until Glance presentation/placement is implemented.

- [ ] **Step 3: Implement Glance composition**

The 320x176 resting composition is:

```text
PowerFlow / current state + Auto-or-lock badge
CPU | PACKAGE | AVG CLOCK
minimal shared trajectory
cause / next-action sentence
```

Use `ShellHeaderControl.Minimal`, `PowerModeControl.CurrentChip`, `LiveStatsControl.Inline`, `TrajectoryControl.Minimal`, and `ControlContextControl.CauseLine`. Navigation and secondary operational context are not shown. `GlanceTapTarget` remains the interaction surface for pinned Glance -> Compact.

- [ ] **Step 4: Preserve hover/pinned semantics and no-activate behavior**

Do not alter the existing `ShellActivationMode.TransientNoActivate` vs `PinnedActive` policy. Hover remains transient; single tray click pins/activates Glance; tapping pinned Glance grows the same HWND to Compact.

- [ ] **Step 5: Run Glance GREEN plus tray/lifecycle contracts**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~GlanceInformationArchitecture|FullyQualifiedName~TrayPopupAndPicker|FullyQualifiedName~TrayTrajectory|FullyQualifiedName~TrayInteraction|FullyQualifiedName~ShellLifecycle"
```

- [ ] **Step 6: Commit Task 6**

```powershell
git add src\PowerFlow.App\Dashboard tests\PowerFlow.App.Tests
git commit -m "feat: derive tray glance from PowerFlow cockpit"
```

---

### Task 7: Add Bounded Truthful Memory and Machine Identity Telemetry

**Files:**
- Create: `src/PowerFlow.Windows/Activity/ISystemMetricsProvider.cs`
- Create: `src/PowerFlow.Windows/Activity/WindowsSystemMetricsProvider.cs`
- Modify: `src/PowerFlow.Windows/Activity/IActivitySource.cs`
- Modify: `src/PowerFlow.Windows/Activity/DashboardTelemetrySource.cs`
- Modify: `src/PowerFlow.App/Dashboard/DashboardViewModel.cs`
- Modify: `src/PowerFlow.App/Dashboard/LiveStatsControl.xaml`
- Modify: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml`
- Create: `tests/PowerFlow.Windows.Tests/Activity/WindowsSystemMetricsProviderTests.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/DashboardViewModelTests.cs`

**Interfaces:**
- Produces `SystemMetricsSnapshot ISystemMetricsProvider.Read()` and extends `DashboardTelemetry` with optional memory/machine data while preserving all existing call sites.

- [ ] **Step 1: Write RED provider and projection tests**

```csharp
[Fact]
public void MemoryPercent_IsDerivedFromNativeMemoryStatus()
{
    var value = WindowsSystemMetricsProvider.CalculateUsedPercent(1000, 250);
    Assert.Equal(75, value, 3);
}

[Fact]
public void ViewModel_ShowsUnavailableInsteadOfInventingMemory()
{
    var vm = new DashboardViewModel();
    vm.Update(snapshot, new DashboardTelemetry(42, 2200, Now));
    Assert.Equal("-", vm.MemoryLabel);
}
```

- [ ] **Step 2: Run RED**

Expected: fail because provider/memory properties do not exist.

- [ ] **Step 3: Add system metrics provider using native Windows memory status**

Create:

```csharp
namespace PowerFlow.Windows.Activity;

public sealed record SystemMetricsSnapshot(double? MemoryUsedPercent, string? MachineName);

public interface ISystemMetricsProvider
{
    SystemMetricsSnapshot Read();
}
```

Implement `WindowsSystemMetricsProvider` with `GlobalMemoryStatusEx` and `Environment.MachineName`:

```csharp
public SystemMetricsSnapshot Read()
{
    double? used = null;
    var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
    if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0)
        used = CalculateUsedPercent(status.TotalPhysical, status.AvailablePhysical);
    return new SystemMetricsSnapshot(used, Environment.MachineName);
}

internal static double CalculateUsedPercent(ulong total, ulong available)
    => total == 0 ? 0 : Math.Clamp((total - Math.Min(total, available)) * 100d / total, 0, 100);
```

- [ ] **Step 4: Extend `DashboardTelemetry` compatibly**

Change the record to:

```csharp
public sealed record DashboardTelemetry(
    double? PackageWatts,
    double? AverageMhz,
    DateTimeOffset At,
    double? MemoryUsedPercent = null,
    string? MachineName = null);
```

Existing three-argument constructors continue to compile.

- [ ] **Step 5: Sample system metrics inside the existing rich telemetry cadence**

Inject an optional provider into `DashboardTelemetrySource` and read it during the existing `Read(at)` call. Do not create a second timer/thread/polling loop.

```csharp
private readonly ISystemMetricsProvider _system;

public DashboardTelemetrySource(ISystemMetricsProvider? system = null)
{
    _system = system ?? new WindowsSystemMetricsProvider();
}

public DashboardTelemetry Read(DateTimeOffset at)
{
    var system = _system.Read();
    return new DashboardTelemetry(
        _energy.TryReadWatts(), TryReadAverageMhz(), at,
        system.MemoryUsedPercent, system.MachineName);
}
```

- [ ] **Step 6: Project memory/machine data into the existing controls**

Add:

```csharp
public double? MemoryPercent => _telemetry?.MemoryUsedPercent;
public string MemoryLabel => MemoryPercent is double value ? $"{value:0}%" : "-";
public string MachineLabel => string.IsNullOrWhiteSpace(_telemetry?.MachineName) ? string.Empty : _telemetry!.MachineName!;
```

`LiveStatsControl` gets a real MEMORY instrument when present and an unavailable state otherwise. `ShellHeaderControl` shows machine identity only when `MachineLabel` is non-empty.

- [ ] **Step 7: Run GREEN plus cadence/overhead architecture tests**

```powershell
dotnet test tests\PowerFlow.Windows.Tests\PowerFlow.Windows.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WindowsSystemMetricsProvider"
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DashboardViewModel|FullyQualifiedName~TelemetryCadence|FullyQualifiedName~TelemetryOwnership"
```

Expected: pass; no new telemetry timer or continuous disk log appears.

- [ ] **Step 8: Commit Task 7**

```powershell
git add src\PowerFlow.Windows src\PowerFlow.App\Dashboard tests\PowerFlow.Windows.Tests tests\PowerFlow.App.Tests
git commit -m "feat: add lightweight system metrics to PowerFlow cockpit"
```

---

### Task 8: Choreograph Interior Morphing Across the Semantic Presentations

**Files:**
- Modify: `src/PowerFlow.App/Dashboard/ShellMotionPolicy.cs`
- Modify: `src/PowerFlow.App/Dashboard/MainWindow.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/ShellHeaderControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/PowerModeControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/LiveStatsControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/ControlContextControl.xaml.cs`
- Modify: `src/PowerFlow.App/Dashboard/TrajectoryControl.xaml.cs`
- Modify: `tests/PowerFlow.App.Tests/Dashboard/ShellMotionPolicyTests.cs`
- Create: `tests/PowerFlow.App.Tests/Dashboard/SemanticMorphContractTests.cs`

**Interfaces:**
- Consumes source and target `ShellPresentationProfile` plus existing `ShellTransitionGeometry` window interpolation.
- Produces component-local morph progress without whole-dashboard crossfades.

- [ ] **Step 1: Write RED timing/sequence tests**

```csharp
[Fact]
public void Expansion_ReformsModesBeforeNavigationArrives()
{
    Assert.True(ShellMotionPolicy.ModeMorphProgress(.35) > 0);
    Assert.Equal(0, ShellMotionPolicy.NavigationProgress(.35));
    Assert.True(ShellMotionPolicy.NavigationProgress(.85) > 0);
}

[Fact]
public void Collapse_RemovesNavigationBeforePrimaryAnchors()
{
    Assert.True(ShellMotionPolicy.NavigationProgress(.30, collapsing: true) <
                ShellMotionPolicy.PrimaryAnchorProgress(.30, collapsing: true));
}
```

- [ ] **Step 2: Run RED**

Expected: fail because semantic interior progress functions do not exist.

- [ ] **Step 3: Add bounded semantic progress functions**

Keep existing durations and easing. Add deterministic progress helpers:

```csharp
public static double ModeMorphProgress(double t) => Window(t, 0.10, 0.65);
public static double StatsMorphProgress(double t) => Window(t, 0.15, 0.80);
public static double ContextMorphProgress(double t) => Window(t, 0.35, 0.90);
public static double NavigationProgress(double t, bool collapsing = false)
    => collapsing ? 1 - Window(t, 0.05, 0.35) : Window(t, 0.65, 0.95);
public static double PrimaryAnchorProgress(double t, bool collapsing = false)
    => collapsing ? 1 - Window(t, 0.55, 0.95) : Window(t, 0.05, 0.90);

private static double Window(double value, double start, double end)
    => Math.Clamp((Math.Clamp(value, 0, 1) - start) / Math.Max(.001, end - start), 0, 1);
```

- [ ] **Step 4: Give primary controls a local morph interface**

Each control adds a method such as:

```csharp
public void ApplyMorph(ModePresentation from, ModePresentation to, double progress, bool reducedMotion)
```

or the corresponding enum for that control. The implementation may overlap local presentation layers while translating/fading them, but must not crossfade the entire dashboard. Keep all layers bound to the same view model.

- [ ] **Step 5: Drive interior morphs from the existing window animation frame**

Capture source/target profiles before `AnimateShellBoundsAsync`. On every frame, continue `AppWindow.MoveAndResize(rect)` and call a new `ApplyShellTransitionFrame(...)`:

```csharp
private void ApplyShellTransitionFrame(
    ShellPresentationProfile from,
    ShellPresentationProfile to,
    double progress,
    bool reducedMotion)
{
    PowerModeBandHost.ApplyMorph(from.Modes, to.Modes, ShellMotionPolicy.ModeMorphProgress(progress), reducedMotion);
    LiveStatsHost.ApplyMorph(from.Stats, to.Stats, ShellMotionPolicy.StatsMorphProgress(progress), reducedMotion);
    ControlContextBandHost.ApplyMorph(from.ControlContext, to.ControlContext, ShellMotionPolicy.ContextMorphProgress(progress), reducedMotion);
    Trajectory.ApplyMorph(from.Trajectory, to.Trajectory, ShellMotionPolicy.PrimaryAnchorProgress(progress), reducedMotion);
    ApplyNavigationMorph(from.Navigation, to.Navigation,
        ShellMotionPolicy.NavigationProgress(progress, ShellMotionPolicy.IsGrowth(from.State, to.State) == false));
}
```

Labels must remain readable during intermediate frames. The stats/control anchors do not disappear simultaneously.

- [ ] **Step 6: Preserve Reduced Motion**

When `ShouldAnimatePresentation()` is false, apply the target profile once with no interpolation. Final information state must match animated mode exactly.

- [ ] **Step 7: Run motion GREEN and lifecycle regression**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ShellMotionPolicy|FullyQualifiedName~SemanticMorph|FullyQualifiedName~ShellTransitionGeometry|FullyQualifiedName~ShellLifecycle|FullyQualifiedName~Readability"
```

- [ ] **Step 8: Commit Task 8**

```powershell
git add src\PowerFlow.App\Dashboard tests\PowerFlow.App.Tests\Dashboard
git commit -m "feat: morph PowerFlow cockpit across shell densities"
```

---

### Task 9: Full Automated Regression on the Exact Candidate

**Files:**
- Modify only tests/contracts proven stale by intentional IA changes; do not weaken behavior requirements.
- No documentation changes.

**Interfaces:**
- Consumes the exact candidate from Tasks 1-8.
- Produces a regression-qualified Release candidate for live visual acceptance.

- [ ] **Step 1: Run all three test projects separately and record actual counts**

```powershell
dotnet test tests\PowerFlow.Core.Tests\PowerFlow.Core.Tests.csproj -c Release --no-restore
dotnet test tests\PowerFlow.Windows.Tests\PowerFlow.Windows.Tests.csproj -c Release --no-restore
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore
```

Expected: zero failures. If a source-contract test describes the superseded boolean/card architecture, rewrite it to assert the corrected spec; do not delete a behavioral safety test merely to make the suite green.

- [ ] **Step 2: Build exact Release app**

```powershell
dotnet build src\PowerFlow.App\PowerFlow.App.csproj -c Release --no-restore
git diff --check
```

Expected: build succeeds with zero errors and `git diff --check` is clean.

- [ ] **Step 3: Scan for forbidden regression patterns**

```powershell
rg -n "TrayHoverWindow|ShowLiveStatsPanel|ShowLowerContextPanels|Top Power Consumers|FontSize=\"(?:8|9|10)\"" src tests
```

Expected: no live product source reintroduces the retired second window, boolean information-loss model, false per-process-watt label, or tiny text. Historical docs may contain old terms and are not product regressions.

- [ ] **Step 4: Commit only necessary regression-contract updates**

```powershell
git add tests
git commit -m "test: qualify reference cockpit architecture"
```

Skip this commit if no files changed.

---

### Task 10: Live Same-HWND Acceptance and User Visual Gate
> **HARD SAFETY GATE:** Task 10 is BLOCKED by the reference host UI Safety rule unless the user gives fresh, explicit authorization for this specific live-UI validation. Do not interpret general requests such as "continue", "proceed", "finish", or prior UI approval as permission to surface UI. Without that authorization, preserve the built candidate and report that live visual acceptance remains pending.

**Files:**
- Create/replace only temporary candidate captures under `docs/assets/` after exact candidate build is known.
- Do not edit README yet.

**Interfaces:**
- Consumes the exact Release executable from Task 9.
- Produces visual evidence and a live app left open for the user's review; does not declare acceptance itself.

- [ ] **Step 1: Verify no ambiguous PowerFlow runtime before launch**

Use process inspection to identify every PowerFlow process and executable path. Stop only obsolete PowerFlow candidate processes intentionally, explaining the operator-induced restart. Do not touch unrelated applications.

- [ ] **Step 2: Launch the exact candidate tray-first and wait for runtime readiness**

Launch:

```powershell
.\src\PowerFlow.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\PowerFlow.App.exe --background
```

Do not assume startup is ready merely because the process exists. Wait until tray/shell runtime state is responsive before interaction.

- [ ] **Step 3: Validate tray -> Glance -> Compact -> Expanded on one HWND**

Observe and record:

1. tray hover or the existing real-tray `--popup-preview` path yields 320x176 Glance anchored at the actual tray icon;
2. single tray click pins Glance without creating another visual window;
3. tapping pinned Glance grows the same HWND to 760x440 Compact;
4. Expand grows the same HWND to approximately 1280x800 Expanded;
5. Compact/Expanded reverse transitions preserve the same HWND and shrink toward the tray anchor;
6. Full Screen extends the same architecture and Restore returns to Expanded.

If automation cannot reliably drive the notification-area icon, do not fake the click. Use the existing app diagnostic path for tray geometry and have the user perform the single click during visual review.

- [ ] **Step 4: Validate the information architecture at each resting state**

At Glance verify: state/control badge, three live values, trajectory, cause/next sentence.

At Compact verify: segmented modes, trajectory ~2/3, live-stats rail ~1/3, control-context rail, compact Rules/Settings/Expand affordances.

At Expanded verify: left rail, slim system header, four semantic mode cards, 7:3 trajectory/live-stats row, three distinct control modules, Recent Events, Active Sources, Quick Actions, quiet status framing.

At Full Screen verify: same regions, more breathing room/detail rather than another dashboard tree.

- [ ] **Step 5: Capture exact candidate views directly from the PowerFlow-owned HWND**

Use `PrintWindow(PW_RENDERFULLCONTENT)` or an equivalent HWND-targeted capture, not screen-coordinate cropping. Capture:

- `docs/assets/powerflow-glance-candidate.png`
- `docs/assets/powerflow-compact-candidate.png`
- `docs/assets/powerflow-expanded-candidate.png`
- `docs/assets/powerflow-fullscreen-candidate.png`

Verify each image's dimensions/nonblank content and the owning PID/executable path.

- [ ] **Step 6: Check crash evidence for the acceptance interval**

Inspect Windows Application/.NET/WER events for the candidate process/start time. Distinguish operator-induced exits from crashes.

- [ ] **Step 7: Leave Expanded open for user inspection and STOP at the visual gate**

The user must review the actual UI and explicitly accept or request changes. Do not merge, tag, publish, rewrite README screenshots, or call the experiment complete before this response.

---

### Task 11: Rewrite Public README Only After Visual Acceptance

**Files:**
- Modify: `README.md`
- Replace accepted public images under: `docs/assets/`
- Create/restore: `tests/PowerFlow.App.Tests/Shell/ReadmeContractTests.cs`

**Interfaces:**
- Consumes the user-approved UI and accepted candidate screenshots.
- Produces public product documentation, not an internal qualification transcript.

- [ ] **Step 1: Add RED README contract**

Assert README contains product purpose, differentiation, installation/build/run, truthful screenshots, concise AI/TDD note, and omits machine names/HWND/test-count chest-thumping/acceptance diary.

- [ ] **Step 2: Rewrite README for the world**

Required structure:

```text
PowerFlow
one-line product proposition
What PowerFlow is
Why it is different
How the tray -> Glance -> Compact -> Expanded cockpit works
What it controls / safety boundary
Install / build / run
Configuration
Current beta limitations
Short AI note
```

The AI note is one short paragraph: PowerFlow is AI-assisted/vibe-coded, but changes are developed with test-driven discipline and accepted behavior is backed by tests. Do not expand into regression statistics.

- [ ] **Step 3: Promote accepted screenshots to stable names**

Use only screenshots from the exact user-approved candidate. Do not reuse beta.4 imagery if the cockpit changed.

- [ ] **Step 4: Run README contract and diff check**

```powershell
dotnet test tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ReadmeContractTests
git diff --check
```

- [ ] **Step 5: Commit Task 11**

```powershell
git add README.md docs\assets tests\PowerFlow.App.Tests\Shell\ReadmeContractTests.cs
git commit -m "docs: present the PowerFlow reference cockpit"
```

---

### Task 12: Final Hygiene, Merge, and Publication Only After User Approval

**Files:**
- No product files unless a final approval fix is required and separately tested.

**Interfaces:**
- Consumes explicit user approval after Task 10 and completed public docs from Task 11.
- Produces a clean canonical repository/runtime and the next beta only after verification.

- [ ] **Step 1: Re-run exact final regression**

Run Core, Windows, App tests and Release build again after README/public-image commit. Record actual results; do not reuse earlier counts.

- [ ] **Step 2: Verify repository hygiene**

Confirm:

- `git status` clean;
- no stale PowerFlow candidate processes;
- no obsolete `TrayHoverWindow` source/generated artifacts after normal clean build;
- no orphaned feature worktrees after integration;
- startup points to the canonical Release executable, not the feature worktree;
- exactly one intended PowerFlow background process after restart.

- [ ] **Step 3: Integrate feature branch into canonical `master` without rewriting historical beta tags**

Use a normal merge/fast-forward according to repository state. Do not move `v0.1.0-beta.4`.

- [ ] **Step 4: Cut the next beta tag only after remote verification**

Create a new annotated beta tag on the accepted commit, push `master` and the new tag, then verify remote master and peeled tag both resolve to the exact accepted commit.

- [ ] **Step 5: Leave the accepted Compact or Expanded PowerFlow UI available and report only verified final facts**

Do not declare RTM/production hardening complete; long-duration controller overhead, hidden Game Latch overhead, real-game frame-time soak, and signed installer/package remain separate release-hardening work unless completed independently.
