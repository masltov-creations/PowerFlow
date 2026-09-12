# reference-host acceptance - 2026-09-11

## Candidate premise

This acceptance record covers the lean PowerFlow product contract: one adaptive AUTO over measured PowerFlow profiles; app Low/Normal/High importance; seven fixed-profile Machine Baseline; production navigation Live / Workloads / Baseline with Settings secondary.

## Automated evidence after canonical reset

The complete post-rewrite gate passed against the repository state represented by this acceptance record:

- PowerFlow.Core.Tests: **50/50**
- PowerFlow.Windows.Tests: **51/51**
- PowerFlow.App.Tests: **370/370**
- Release solution build: **0 warnings / 0 errors**
- source XAML parse: **9/9**
- `git diff --check`: clean
- removed production surfaces/references: absent for Compare, Atlas, Tune Auto, advisory service policy, and their presentation/runtime helpers

The App test count is intentionally lower than the pre-reset candidate because tests belonging solely to deleted Atlas/Compare/Tune/service-policy product surfaces were removed with those surfaces. Current product-surface regression tests instead assert their absence and the Live / Workloads / Baseline + Settings contract.

## Live seven-profile baseline

A fresh standard seven-profile run completed successfully on reference-host in this order:

1. Windows Saver
2. Windows Balanced
3. PF SAVER
4. BAL-E
5. BAL-P
6. PERF
7. ULTRA

Observed distinguishing signatures included:

- PF SAVER: Power Saver, core floor 10, EPP 60, boost 0.
- ULTRA: High Performance, core floor 100, EPP 10, boost 2.

The run recommended BAL-E under the current recommendation rule.

## Restoration evidence

Pre-run and post-run processor policy matched exactly:

- Windows plan: Balanced (`381b4222-f694-41f0-9685-ff5bb260df2e`)
- core floor: 25
- EPP: 35
- boost mode: 3

## Runtime evidence

After the qualification run, the Release app launched as a single healthy PowerFlow process with a responding PowerFlow window and no new startup error. Visible UI validation was explicitly authorized in the session that produced this evidence.

## Scope

This record proves the 2026-09-11 behavior and restoration gates. Older UI screenshots and earlier acceptance runs are retained under `docs/history` and must not be treated as the current product contract.

## Current Live UI capture

- Relaunched the qualified Release build on reference-host with --fullscreen on September 11, 2026.
- Confirmed the visible PowerFlow window and allowed more than 60 seconds of live telemetry to accumulate before capture, matching the Live timeline's 60-second window.
- Refreshed docs/assets/powerflow-fullscreen.png at 1920x1080 from the populated Live view.
- Screenshot SHA-256: 5d83cb7a2f1f7bd9c690715f86fa01448871539882f72255f904794eebec2f5a.
- PowerFlow remained responsive after capture; no launch-only frame was used.
- The refreshed Live UI explicitly labels projected envelope state as **MODEL ZONE** and separately reports the actual applied PowerFlow profile (`AUTO / SAVER`, `AUTO / BAL-E`, `AUTO / BAL-P`, or `AUTO / PERF`; ULTRA remains manual-only). This prevents model-zone motion from being mistaken for verified profile switching.

## Visual coherence and shell-motion acceptance

- Light and Dark themes were exercised across Live, Workloads, Baseline, and Settings at 1280x800: zero visible clipping and zero undersized interactive controls; Follow Windows was restored afterward.
- Workloads `Add app` now renders at 79x32 in the live UI. Shell and power-mode actions use a 34px logical minimum so fractional resize/DPI rounding does not collapse them below the 32px physical target.
- Manual Live resize continuously morphs between Compact and Expanded from 760x440 through 900x560; the prior 860x520 density cliff no longer changes the visible composition discontinuously. The navigation rail collapses continuously to zero at the Compact endpoint.
- Native high-cadence transition evidence on the final build: Full Screen->Compact 1920x1080->760x440, 37 distinct native sizes, max sampled step 88x48, zero reversals; Compact->Expanded 760x440->1280x800, max sampled step 76x53, zero reversals; Expanded->Full Screen 1280x800->1920x1080, max sampled step 79x34, zero reversals.
- Stable Full Screen, Expanded, and Compact endpoints each audited with zero visible clipping and zero undersized controls. The `NavigationRail` automation wrapper is excluded from clipping counts because NavigationView reports its un-clipped desired bounds rather than visible child bounds during responsive morphs.
- Final canonical screenshot was captured from the same accepted Release process after 72.9 seconds of runtime, with the 60-second Live telemetry history populated; the process remained responsive after capture.