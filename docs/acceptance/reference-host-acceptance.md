# reference-host acceptance - 2026-09-09

## Candidate

- Current code-under-test commit: `b40fdc4` (`feat: add progressive PowerFlow presentation shell`)
- Branch: `master`
- Build: Release x64, unpackaged WinUI 3
- Machine: reference-host

## Automated gate

Current full regression after the progressive-shell change:

- PowerFlow.Core.Tests: 13 passed, 0 failed
- PowerFlow.Windows.Tests: 29 passed, 0 failed
- PowerFlow.App.Tests: 127 passed, 0 failed
- Total: 169 passed, 0 failed
- Release build: 0 warnings, 0 errors
- `git diff --check`: clean

The new presentation contract was developed RED -> GREEN: 4 new tests initially failed against the fixed-size shell and now pass.

## WinUI crash regression

Earlier runtime failures in `Microsoft.UI.Xaml.dll` were traced to dynamic drawing code directly indexing theme resource dictionaries.

Regression proof:

1. Detached pre-fix commit `2d07b61` + safety tests: 2 failed, 0 passed.
2. Named XAML brush sources: 2 passed, 0 failed.
3. Full suite passed afterward.

Fix commit: `30e7fc2`.

## Process-disappearance clarification

A user-observed app disappearance immediately after the earlier acceptance run was not an independent PowerFlow crash. The final hygiene migration job intentionally stopped the old feature-worktree process and restarted PowerFlow from the `master` worktree. Post-migration Windows Application/.NET/WER crash-event count was 0.

## Progressive presentation acceptance

The Release executable was started tray-first and the dashboard opened through the single-instance relay.

Live measured states:

- compressed open: 760 x 440;
- expanded resize: 1120 x 720;
- collapse: 760 x 440;
- compressed screenshot: `docs/assets/powerflow-compressed.png`;
- expanded screenshot: `docs/assets/powerflow-expanded.png`;
- new Application/.NET/WER PowerFlow crash events during the transition run: 0.

The UI source contract also fixes the tray-hover instrument to 320 x 176 and removes the previous dedicated three-column telemetry band in favor of one compact telemetry line plus the same trajectory vocabulary.

## Telemetry semantics

The dashboard's frequency source is `CallNtPowerInformation(SystemProcessorPowerInformation)` averaged across active processors. Independent Windows telemetry on reference-host reported the same value at validation time:

- `Win32_Processor.CurrentClockSpeed`: 1746 MHz;
- `Processor Information(_Total)\\Processor Frequency`: 1746 MHz;
- PowerFlow source: average `CurrentMhz` from the same processor-power state family.

The UI now labels the value `AVG CLOCK` to distinguish it from per-core peak/boost frequency.

## Native power-plan acceptance

The exact production `PowerFlow.Windows.Power.WindowsPowerPlanController` was loaded from the Release build.

1. Original: Power Saver (`a1841308-3541-4fab-bc81-f71556f20b4a`).
2. `ActivateAsync(Balanced)` succeeded and Windows independently reported Balanced (`381b4222-f694-41f0-9685-ff5bb260df2e`).
3. `ActivateAsync(PowerSaver)` succeeded and Windows independently reported Power Saver (`a1841308-3541-4fab-bc81-f71556f20b4a`).

This verifies that normal-mode commands reach a real, verified Windows scheme switch. Preview mode remains deliberately non-mutating.

## Remaining release-hardening work

Not claimed by this acceptance record:

- 120-second controller-off vs controller-on package-power baseline;
- 120-second hidden Game Latch overhead measurement;
- real-game frame-time correlation/soak;
- signed installer/package.

These remain production-hardening gates, not hidden claims.