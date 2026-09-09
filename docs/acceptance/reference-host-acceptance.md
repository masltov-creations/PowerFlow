# reference-host acceptance - 2026-09-09

## Candidate

- Code-under-test commit before release docs: `30e7fc2` (`fix: resolve dynamic theme brushes safely`)
- Branch under test: `feature/powerflow-v1`
- Build: Release x64, unpackaged WinUI 3
- Machine: reference-host

## Automated gate

Fresh full headless verification after the theme-resource fix:

- PowerFlow.Core.Tests: 13 passed, 0 failed
- PowerFlow.Windows.Tests: 29 passed, 0 failed
- PowerFlow.App.Tests: 123 passed, 0 failed
- Total: 165 passed, 0 failed
- Release build: 0 warnings, 0 errors
- `git diff --check`: clean
- PowerFlow process count during headless gate: 0

## WinUI crash regression

Recent runtime evidence showed repeated `PowerFlow.App.exe` failures in `Microsoft.UI.Xaml.dll` while dynamic drawing code directly indexed theme resource dictionaries.

Regression proof:

1. Detached pre-fix commit `2d07b61` + the new safety test: 2 failed, 0 passed.
2. Candidate with named XAML brush sources: 2 passed, 0 failed.
3. Full suite then passed 165/165.

The fix is commit `30e7fc2`.

## Live tray/dashboard acceptance

Release executable was launched with `--background`.

- after 25 seconds hidden: process alive, main-window handle = 0;
- dashboard request relayed to the primary process;
- dashboard appeared at 960 x 620;
- screenshot captured to `docs/assets/powerflow-dashboard.png`;
- screenshot PNG validated as 960 x 620 and nonblank;
- first close returned to tray-only state;
- dashboard reopened after another hidden interval;
- second close returned to tray-only state;
- new Application/.NET/WER PowerFlow crash events: 0;
- final process count: 1.

## Native power-plan acceptance

With the tray process temporarily stopped, the exact production `PowerFlow.Windows.Power.WindowsPowerPlanController` was loaded from the Release build.

1. Original: Power Saver (`a1841308-3541-4fab-bc81-f71556f20b4a`).
2. `ActivateAsync(Balanced)` returned success and active GUID `381b4222-f694-41f0-9685-ff5bb260df2e`; `powercfg /getactivescheme` independently reported Balanced.
3. `ActivateAsync(PowerSaver)` returned success and active GUID `a1841308-3541-4fab-bc81-f71556f20b4a`; `powercfg /getactivescheme` independently reported Power Saver.
4. Release tray process restarted hidden; final PowerFlow process count = 1.

This verifies that normal-mode policy commands are connected to a real, verified Windows scheme switch. Preview mode remains deliberately non-mutating.

## Remaining release-hardening work

Not claimed by this acceptance record:

- 120-second controller-off vs controller-on package-power baseline;
- 120-second hidden Game Latch overhead measurement;
- real-game frame-time correlation/soak;
- signed installer/package.

These are hardening gates for a later production release. They are not blockers to publishing the current source as a working beta.