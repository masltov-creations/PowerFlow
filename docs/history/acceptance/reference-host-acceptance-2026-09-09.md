# reference-host acceptance — 2026-09-09

## Candidate

- Code under test: `84bdc98` — `feat: make PowerFlow shell fluid and fullscreen`
- Branch: `master`
- Build: Release x64, unpackaged WinUI 3
- Machine: reference-host

## Automated gate

Final regression on the exact responsive/full-screen code:

- PowerFlow.Core.Tests: 13 passed, 0 failed
- PowerFlow.Windows.Tests: 29 passed, 0 failed
- PowerFlow.App.Tests: 137 passed, 0 failed
- **Total: 179 passed, 0 failed**
- Release build: **0 warnings, 0 errors**
- `git diff --check`: clean

The responsive work was test-driven. The new RED contract failed because `DashboardResponsiveLayout` and `DashboardPresentationMode.FullScreen` did not exist. After implementation, targeted responsive/presentation/startup tests passed, followed by the complete suite above.

## Responsive presentation acceptance

The previous beta had distinct compact and expanded sizes, but did not genuinely reflow enough content within a mode. This candidate replaces that with `DashboardResponsiveLayout.Resolve(width, height, fullScreenPresenter)`.

Live Win32 resize acceptance exercised the real Release window at:

1. 760 × 440
2. 980 × 620
3. 1120 × 720
4. 1320 × 820
5. 1600 × 900

Every requested size was observed exactly and the process remained alive throughout. The layout engine changes graph minimum height, reason width, panel padding, header/context spacing, telemetry presentation, typography, hover-lens width, and disclosure density from the actual dimensions rather than only switching between two fixed compositions.

The pure layout tests additionally prove that 980 × 620 and 1320 × 820 are both `Expanded`, while the larger size receives larger graph, reason-width, and padding values. Large manual windows can reach full-density presentation without forcing the full-screen presenter.

## Four connected levels

### 1. Tray hover

- real `TrayHoverWindow` shown through the normal `ShowAsync` path;
- release preview waits for the actual `Shell_NotifyIconGetRect` icon rectangle and uses its real monitor work area;
- visible PID-owned HWND: `265710`;
- measured size: **320 × 176**;
- direct `PrintWindow` capture: `docs/assets/powerflow-popup.png`;
- PNG size: 11,745 bytes;
- sampled unique colors: 73 (nonblank check).

### 2. Compressed

- measured size: **760 × 440**;
- direct PowerFlow HWND capture: `docs/assets/powerflow-compressed.png`;
- captured HWND during acceptance: `11600966`;
- compact telemetry strip is used instead of shrinking the expanded metric card;
- all explicitly sized visible text remains at least 11 px.

### 3. Expanded

- canonical working size remains **1120 × 720**;
- presentation is fluid rather than fixed: intermediate and larger dimensions continuously alter the layout profile;
- the same trajectory, NOW state, policy nodes, and context vocabulary remain anchored.

### 4. Full screen

- entered through `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)`;
- measured window rectangle: **1920 × 1080 at (0,0)**;
- direct PowerFlow HWND capture: `docs/assets/powerflow-fullscreen.png`;
- captured HWND during acceptance: `5769704`;
- PNG size: 51,695 bytes;
- sampled nonblank validation passed;
- full-density context becomes visible instead of simply stretching the expanded layout.

## Screenshot integrity

The earlier beta.2 screenshot evidence was invalid because it used screen-coordinate capture. Beta.3 corrected that problem for dashboard captures. This candidate extends the rule to every release image:

- identify the PowerFlow process;
- identify a visible HWND owned by that PID;
- verify the expected window dimensions;
- capture the HWND directly with `PrintWindow(PW_RENDERFULLCONTENT)`;
- validate the resulting PNG is nonblank.

No release screenshot in the current README depends on desktop coordinates.

## Runtime stability

During the live responsive/full-screen acceptance run:

- Windows Application/.NET/WER PowerFlow crash events: **0**;
- the process survived all five manual resize points;
- true full-screen capture completed successfully.

The prior user-observed “crash” after an older acceptance run was separately proven to be an operator-induced process replacement during worktree cleanup, not an independent PowerFlow crash.

## WinUI crash regression

Earlier `Microsoft.UI.Xaml.dll` crashes were traced to dynamic drawing code directly indexing theme resource dictionaries.

Regression proof:

1. detached pre-fix commit `2d07b61` plus safety tests: 2 failed, 0 passed;
2. named XAML brush sources: 2 passed, 0 failed;
3. full suite passed afterward.

Fix commit: `30e7fc2`.

## Telemetry semantics

`AVG CLOCK` comes from `CallNtPowerInformation(SystemProcessorPowerInformation)` averaged across active processors. During validation, independent Windows sources both reported 1746 MHz:

- `Win32_Processor.CurrentClockSpeed`: 1746 MHz;
- `Processor Information(_Total)\Processor Frequency`: 1746 MHz.

The label intentionally says `AVG CLOCK`; it is not a fastest-core boost-clock claim.

## Native power-plan acceptance

The exact production `PowerFlow.Windows.Power.WindowsPowerPlanController` was loaded from the Release build:

1. original active scheme: Power Saver (`a1841308-3541-4fab-bc81-f71556f20b4a`);
2. `ActivateAsync(Balanced)` succeeded and Windows independently reported Balanced (`381b4222-f694-41f0-9685-ff5bb260df2e`);
3. `ActivateAsync(PowerSaver)` succeeded and Windows independently reported Power Saver again.

Normal mode therefore reaches a real, independently verified Windows scheme switch. `--preview` remains deliberately non-mutating.

## Remaining production-hardening work

Not claimed by this beta:

- 120-second controller-off vs controller-on package-power baseline;
- 120-second hidden Game Latch overhead measurement;
- real-game frame-time correlation/soak;
- signed installer/package.

Those remain production-hardening gates, not README magic tricks.