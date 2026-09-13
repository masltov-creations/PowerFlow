# PowerFlow behavior

This document describes how the current PowerFlow features behave.

## Operating modes

PowerFlow has one automatic mode and five manual profiles.

### Auto

Auto uses recent telemetry, the learned operating envelope, and app importance to decide how much CPU performance the current work needs. It can select:

| Model zone | Applied profile |
| --- | --- |
| Eco | SAVER |
| Efficient | BAL-E |
| Responsive | BAL-P |
| Boost | PERF |

ULTRA is not selected by Auto.

A model-zone change is not the same thing as a profile change. The model zone is the current interpretation of demand; the applied profile is the processor policy PowerFlow has actually written to Windows.

### Manual profiles

Selecting a manual profile holds that profile until Auto is selected again.

| Profile | Windows plan | Core floor | EPP | Boost |
| --- | --- | ---: | ---: | ---: |
| SAVER | Power Saver | 10% | 60 | 0 |
| BAL-E | Balanced | 25% | 35 | 3 |
| BAL-P | Balanced | 50% | 20 | 3 |
| PERF | Balanced | 75% | 10 | 2 |
| ULTRA | High Performance | 100% | 10 | 2 |

## Live view

Live combines recent machine telemetry with PowerFlow's current decision state. It presents:

- CPU activity over time
- physical-core activity
- package power when available
- effective clock/performance data when available
- current Model Zone
- current applied PowerFlow profile
- the workload or rule influencing the decision
- recent movement through the operating range

Live uses a rolling history window rather than a launch-only snapshot.

## App importance

Workloads lets an app be marked Low, Normal, or High.

| Importance | Behavior |
| --- | --- |
| Low | Background/non-urgent. Cannot cause a move into PERF by itself. |
| Normal | Default. May reach PERF after sustained demand qualifies. |
| High | Latency-sensitive. May reach PERF with shorter qualification. |

App importance influences Auto but does not create a permanent manual lock.

## Baseline Machine

The standard baseline runs seven five-minute legs:

1. Windows Power Saver
2. Windows Balanced
3. PowerFlow SAVER
4. PowerFlow BAL-E
5. PowerFlow BAL-P
6. PowerFlow PERF
7. PowerFlow ULTRA

For each leg, PowerFlow records the applied policy, idle package power when available, and a synthetic CPU throughput curve using 1, 2, 4, 8, and 16 workers.

The result view overlays the profiles so differences in idle power, throughput, efficiency, and the throughput knee are easy to compare. The recommendation favors profiles that reach near-maximum throughput without paying unnecessary idle-power cost.

### Core-concentration characterization

Baseline must also be able to determine whether concentrating work onto fewer awake cores improves useful performance because of boost, voltage, power, or thermal headroom. This is a controlled characterization test, not an inference from the normal profile comparison.

For this test, PowerFlow holds the Windows plan, EPP, boost policy, workload kernel, and other processor-policy variables constant while varying the amount of CPU capacity made available to the workload. The tested core-availability points are derived from the machine's physical-core count and must include low, intermediate, and full-core cases; they must not assume a fixed CPU topology. The same 1, 2, 4, 8, and 16 worker points are used where the machine has enough logical processors.

Each point must record:

- measured throughput and throughput per watt
- package power when available
- actual awake and parked physical/logical cores, not only the configured parking floor
- effective/per-core frequency or the best reliable equivalent
- processor-performance percentage
- CPU temperature when a reliable source is available
- VID/Vcore or another voltage indicator only when a reliable source is available
- the exact power and scheduling policy used for the sample

The analysis must separately identify single-thread boost behavior, partial-load/core-concentration behavior, the scaling knee, and any thermal or power-pressure point where waking additional cores stops improving throughput. A configured minimum-core parking percentage is not evidence that those cores actually remained parked, so observed residency is required for conclusions about parking.

If deterministic affinity or CPU-set constraints are needed to prove the hardware effect, they may be used for characterization only. Runtime PowerFlow policy should prefer normal Windows scheduling and parking controls unless separately qualified evidence shows that a stronger constraint is safe and useful.

The learned result is a machine-specific core-scaling map: for each measured workload width, PowerFlow can identify the smallest awake-core range that preserves or improves useful throughput, along with its power and thermal cost. Auto may consume this map only after the result is repeatable and qualified; a single run must not become a hard runtime rule.

Before the run, PowerFlow records the active Windows plan and processor policy. That state is restored after completion, cancellation, or failure.

## Settings

PowerFlow currently exposes three general settings:

- theme
- reduced motion
- start with Windows

Per-app importance is managed in Workloads. CPU behavior is controlled by Auto or the manual profile selector.

## Configuration compatibility

PowerFlow can read configuration files created by older builds. Old fields that no longer have a user-facing feature are ignored or normalized when the configuration is loaded so they do not affect current behavior unexpectedly.

## Safety boundaries

PowerFlow changes Windows power plans and processor policy. It does not modify BIOS settings, voltage, fan curves, or firmware.

Manual selection takes precedence over Auto until Auto is selected again. A game latch from older configuration can also temporarily take precedence while that tracked game is active.
