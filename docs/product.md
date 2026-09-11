# PowerFlow Current Product Contract

Status: **canonical**
Updated: **2026-09-11**

This document is the product truth for the current PowerFlow branch. Dated plans and specs under `docs/history` describe how the product evolved; they do not override this contract.

## Purpose

PowerFlow minimizes unnecessary CPU energy, heat, and noise while preserving useful responsiveness. It does this by observing demand, classifying the justified response, and applying one of a small set of measured operating profiles.

PowerFlow is not a generic Windows power-plan editor and is not an expert tuning console.

## User model

The normal user chooses between:

- **AUTO**: PowerFlow owns profile selection.
- **SAVER**: explicit low-power manual profile.
- **BAL-E**: explicit balanced-efficient manual profile.
- **BAL-P**: explicit balanced-performance manual profile.
- **PERF**: explicit high-readiness manual profile.
- **ULTRA**: explicit maximum-readiness manual profile.

A manual profile is an override. Selecting AUTO releases manual authority and returns profile selection to the adaptive governor.

## Fixed profiles

| Profile | Windows state | Core floor | EPP | Boost | Readiness floor |
| --- | --- | ---: | ---: | ---: | ---: |
| SAVER | Power Saver | 10% | 60 | 0 | 12% |
| BAL-E | Balanced | 25% | 35 | 3 | 25% |
| BAL-P | Balanced | 50% | 20 | 3 | 50% |
| PERF | Balanced | 75% | 10 | 2 | 75% |
| ULTRA | High Performance | 100% | 10 | 2 | 100% |

AUTO may use SAVER, BAL-E, BAL-P, and PERF. ULTRA is manual-only.

## AUTO

AUTO has one authority path:

`telemetry -> adaptive envelope -> entitlement/confidence gate -> semantic zone -> PowerFlow profile -> verified Windows/profile actuation`

Live deliberately separates **MODEL ZONE** from the **applied PowerFlow profile**. MODEL ZONE is the envelope's interpretation of demand and may change while the governor qualifies, leases, or brakes. The applied profile is the real SAVER / BAL-E / BAL-P / PERF / ULTRA processor policy currently written to Windows.

The semantic mapping is fixed:

- Eco -> SAVER
- Efficient -> BAL-E
- Responsive -> BAL-P
- Boost -> PERF

AUTO does not have a second CPU-threshold state machine, and AUTO is not represented as a fixed benchmark profile.

The adaptive envelope may learn from retained observations, but user-facing raw boundary/timer/counterfactual editing is not part of the product. Legacy persisted Tune/pause state is normalized back to the current learned/default behavior when loaded by the app.

## Workloads

Application policy is expressed only as **Low**, **Normal**, or **High** importance.

| Importance | Product meaning |
| --- | --- |
| Low | Efficiency-biased. The app cannot promote the machine into Boost by itself. |
| Normal | Default. The app can earn qualified Boost after sustained demand. |
| High | Latency-sensitive. The app can earn Boost with shorter qualification. |

Unclassified applications behave as Normal.

New importance rules are explicit importance rules, not legacy game/performance rules, and therefore do not trigger the old hard Performance game latch.

Custom entitlement timing, process-tree controls, and service-specific policies are not current user features.

## Baseline

Machine Baseline is a controlled characterization run, not AUTO tuning.

The standard schedule is seven fixed profiles x five minutes = 35 minutes:

1. Windows Power Saver
2. Windows Balanced
3. PF SAVER
4. BAL-E
5. BAL-P
6. PERF
7. ULTRA

Each leg records the observed policy signature and both idle and synthetic-load evidence. The synthetic curve uses 1, 2, 4, 8, and 16 workers. Recommendation logic favors the lowest idle-power profile among profiles reaching at least 95% of global maximum throughput, with efficiency fallback when idle evidence is unavailable.

The runner must snapshot and restore the exact pre-run processor policy even on cancellation or failure.

## UI contract

Top-level production navigation is exactly:

- **Live**
- **Workloads**
- **Baseline**

**Settings** is secondary navigation.

The following are explicitly not production surfaces:

- Model / Performance Atlas
- Tune Auto
- Compare / manual A-B efficiency experiment
- service policy editor
- custom entitlement editor
- raw CPU promotion/quiet/cooldown controls
- telemetry cadence controls
- Windows power-plan GUID mapping

Internal mechanisms may exist only when they support qualified visible behavior or backward-compatible configuration loading. They must not create a second behavioral owner.

## Settings contract

Settings exposes only:

- theme;
- reduced motion;
- start with Windows.

## Release contract

A candidate is releasable only when:

1. Core, Windows, and App tests pass.
2. Release build succeeds with no errors.
3. XAML parses.
4. `git diff --check` is clean.
5. Product-surface regression tests prove removed surfaces remain absent.
6. Live UI/runtime validation is performed when explicitly authorized.
7. Any baseline run proves exact policy restoration.
8. Repository docs and Git remote reflect the same candidate.