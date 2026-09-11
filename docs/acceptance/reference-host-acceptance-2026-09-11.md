# reference-host acceptance - 2026-09-11

## Candidate premise

This acceptance record covers the lean PowerFlow product contract: one adaptive AUTO over measured PowerFlow profiles; app Low/Normal/High importance; seven fixed-profile Machine Baseline; production navigation Live / Workloads / Baseline with Settings secondary.

## Automated evidence after canonical reset

The complete post-rewrite gate passed against the repository state represented by this acceptance record:

- PowerFlow.Core.Tests: **50/50**
- PowerFlow.Windows.Tests: **51/51**
- PowerFlow.App.Tests: **350/350**
- Release solution build: **0 warnings / 0 errors**
- source XAML parse: **18/18**
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