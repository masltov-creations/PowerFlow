# PowerFlow Demand / Capacity Instrument v1

## Objective
Make the live dashboard explain workload demand, available CPU capacity, and why a future dynamic policy would add or remove capacity. This slice is observational only: the existing controller still actuates from legacy CPU busy percentage.

## Raw signals retained
- Per-logical-processor `% Processor Utility`
- Per-logical-processor `Processor Frequency`
- Per-logical-processor `% of Maximum Frequency`
- Per-logical-processor `Parking Status`
- System `Processor Queue Length`
- Package power and aggregate effective clock

## Explainable pressure
For each rich telemetry sample, logical processors are grouped by physical core. Physical-core load is the sum of unparked sibling utility, clamped to 100%.

- **Machine demand** = physical-core load summed across all cores / total physical cores.
- **Available capacity** = unparked logical processors weighted by current `% of Maximum Frequency`, expressed as a percentage of full-machine nominal capacity.
- **Queue pressure** = runnable queue length / awake physical cores, mapped so one queued runnable thread per awake core equals 100% queue pressure.
- **Capacity saturation** = delivered work / observed available capacity.
- **Observed pressure** = max(machine demand, capacity saturation, queue pressure).
- **Driver** = whichever component currently determines observed pressure.

There is no weighted composite. Hover/readout must expose the component values so the pressure number is auditable.

## Core-capacity lane
The existing time-aligned physical-core microtiles remain. Active cells now encode load intensity in four bands (<25%, 25-49%, 50-74%, 75%+), while awake-idle and parked remain distinct. The renderer uses persistent Path geometries, not one XAML element per cell.

Per-core frequency and percent-of-maximum-frequency are retained in telemetry and summarized in the core tooltip for later policy work.

## Safety / promotion gate
Do not use Observed Pressure for automatic actuation until it has been compared against real workloads and its thresholds/timing have been explicitly accepted. The dashboard/dry-run may use the new pressure now; controller actuation remains legacy CPU busy percentage.
## Graduated performance entitlement

PowerFlow must not reduce CPU actuation to a binary capped/performance choice or only switch among coarse Windows power plans. The long-term control target is a continuous 0-100 performance entitlement (capacity target) that can move gradually as observed pressure, ramp rate, burst age, sustained demand, and recovery evolve.

Envelope zones remain useful descriptive regions, but they are not the actuator granularity. A zone can contain a range of performance entitlements. For example, Responsive may span many intermediate capacity targets rather than mapping to the same static Balanced state as Efficient.

The eventual actuator should be able to express independent or coordinated targets for:
- available core capacity / parking aggressiveness;
- processor performance preference or equivalent frequency-performance bias where Windows exposes a durable control;
- boost qualification, burst allowance, lease duration, and release hysteresis;
- processor performance floor/ceiling when supported safely;
- package power constraints only where a verified, reversible control surface exists.

The dashboard must distinguish requested entitlement from delivered capacity. It should show both on the common timeline so we can see whether the OS/hardware actually delivered the requested cores/frequency/boost and whether pressure subsequently rose or fell.

Initial implementation remains observation/dry-run only. Do not replace the existing power-plan actuator until the graduated policy has been measured against real workloads and each control surface is verified to be reversible and stable on reference host.
### Dry-run graduated control law v1

The observational capacity controller derives a requested entitlement for every rich telemetry sample. It is intentionally inspectable rather than a hidden score:
- delivered capacity is the observed available-capacity percentage from unparked logical processors weighted by current `% of Maximum Frequency`, clamped to the 0-100 entitlement range;
- sustained pressure is a cadence-independent exponential moving average with a 6-second time constant;
- ramp projects the current pressure slope 1.5 seconds forward;
- a pressure excursion above 70% accumulates burst age and progressively earns additional headroom for up to four seconds;
- stress maps continuously from a 60% target saturation at low stress toward 40% at high stress;
- ideal requested capacity is machine demand divided by target saturation plus explicit runnable-queue reserve;
- requested capacity can rise at most 30 percentage points/second and releases at most 6 points/second, making asymmetric recovery/hysteresis visible.

The dashboard overlays requested and delivered capacity as two continuous curves on the core-capacity lane while retaining the physical-core load/parking microtiles. This remains dry-run only.
### Windows graduated-actuation control audit

Read-only inspection on reference host confirms Windows exposes graduated processor controls beneath the three named plans. Current AC endpoints are:

| Control | Power Saver | Balanced | High Performance | Candidate role |
| --- | ---: | ---: | ---: | --- |
| Core parking minimum (`CPMINCORES`) | 10% | 100% | 100% | Minimum awake-core floor; not an exact core-count command |
| Energy/performance preference (`PERFEPP`) | 60% | 10% | 10% | Continuous performance-vs-efficiency bias |
| Minimum processor state | 5% | 5% | 100% | Avoid as primary graduated control; high values create persistent power cost |
| Maximum processor state | 100% | 100% | 100% | Leave at 100 initially; not what differentiates the existing plans |
| Boost policy (`PERFBOOSTPOL`) | 0% | 60% | 100% | Continuous burst/boost allowance |
| Boost mode | Aggressive | Aggressive | Aggressive | Existing schemes do not differ here; keep stable initially |

Additional hidden controls are present for core-parking increase/decrease time and policy (`CPINCREASETIME`, `CPDECREASETIME`, `CPINCREASEPOL`, `CPDECREASEPOL`), core headroom/overutilization (`CPHEADROOM`, `CPOVERUTIL`), performance increase/decrease thresholds and timing (`PERFINCTHRESHOLD`, `PERFDECTHRESHOLD`, `PERFINCTIME`, `PERFDECTIME`), and related autonomous/performance-history behavior.

#### Actuation principle

Do not assume that a Windows setting percentage equals delivered capacity. For example, Power Saver currently has `CPMINCORES=10%` while the observed machine may still deliver roughly half the physical cores. Graduated actuation should therefore be closed-loop:

1. The dry-run control law requests a 0-100 capacity entitlement.
2. A future actuator translates that request into conservative changes to parking floor, EPP, boost policy, and timing controls.
3. PowerFlow observes delivered capacity from real parking/frequency telemetry.
4. The actuator nudges settings only as needed to converge delivered capacity toward requested capacity.
5. Ramp-up may be faster than release, with explicit burst lease and hysteresis.

Initial candidate ownership is:
- **core availability:** `CPMINCORES` plus parking increase/decrease timing and thresholds, adjusted by feedback rather than treated as an exact core count;
- **frequency/performance bias:** `PERFEPP` first, preserving `PROCTHROTTLEMAX=100` during early experiments;
- **burst allowance:** `PERFBOOSTPOL`, with boost mode held constant initially;
- **time to spike / time to release:** PowerFlow qualification/lease/hysteresis plus the Windows increase/decrease timing controls, tested independently before composition.

No live mutation of these controls is part of v1. Each control must have a snapshot/restore path and isolated stability test before adaptive actuation is allowed to write it.
### Live actuator sandbox calibration — reference host / Power Saver

The first reversible live characterization used the compiled `WindowsProcessorPolicyController`, with exact snapshot/readback and `finally` restoration on every run. Baseline values were `CPMINCORES=10` and `PERFEPP=60`.

- EPP-only sweep `60 -> 45 -> 30 -> 60`: delivered capacity stayed about 23-24% and awake frequency stayed about 1746 MHz under the observed workload. This is not sufficient evidence to qualify EPP as an actuator on the current path. reference host currently reports `PERFAUTONOMOUS=0`; EPP remains HOLD until the performance-state/autonomous path is characterized separately.
- Core-floor `10 -> 25 -> 50 -> 10`: 25% remained below the organically awake population; 50% only slightly exceeded it, confirming `CPMINCORES` acts as a floor rather than a delivered-capacity command.
- Core-floor `10 -> 75 -> 10`: awake logical processors moved 14 -> 24 -> 14, delivered capacity moved 22.3% -> 38.2% -> 22.3%, and capacity saturation moved about 49% -> 31-35% -> 52% under roughly comparable demand. This qualifies core-floor control for the next guarded closed-loop stage.

The advisory actuator translator therefore normalizes requested capacity by current percent-of-maximum-frequency to estimate a required logical-processor floor, uses a 3-point delivery deadband, rises at no more than 15 core-floor percentage points per control step, and releases at no more than 10 points per step. EPP is explicitly held.
### Guarded closed-loop core-floor actuator v1

The first live graduated actuator is deliberately narrow. It owns only `CPMINCORES` on the qualified Power Saver scheme. It never writes EPP, boost policy, processor min/max state, or parking timing controls.

Hard gates:
- separate `GraduatedCoreActuationEnabled` flag, default off;
- preview mode cannot write;
- legacy/coarse `AdaptiveActuationEnabled` and graduated core-floor actuation are mutually exclusive;
- only the configured Power Saver scheme is qualified; scheme changes restore the qualified baseline and suspend writes;
- baseline core floor is captured before the first write and restored on disable, shutdown, scheme change, or fault;
- floor cannot be lowered below the captured baseline or raised above the experimentally qualified 75% ceiling;
- one planner step is at most +15/-10 points and writes are separated by at least five seconds of delivered-capacity feedback;
- every write is read back; a write/readback fault restores baseline and fault-latches the actuator;
- EPP remains HOLD/unqualified.

The loop runs from background rich telemetry, not dashboard visibility. The dashboard receives actuator status only for explanation.
#### Live closed-loop qualification evidence

On reference host / Power Saver, the guarded runtime was exercised against the real processor-policy API and live delivered-capacity telemetry. Baseline was `CPMINCORES=10`, `EPP=60`. With a fixed 35% requested-capacity target, delivered capacity naturally moved between roughly 31% and 37.5%. The actuator correctly held while delivered capacity was sufficient. When delivered capacity fell to ~31.2%, it applied one bounded `10 -> 25` core-floor step while holding EPP at 60. Three seconds later it planned a further step but the five-second write cooldown blocked it. `StopAndRestore()` then returned the policy to `CPMINCORES=10`, `EPP=60` with readback verification.

This qualifies the core-floor feedback loop and its first safety envelope. It does not qualify EPP, boost, >75% core-floor writes, or removal of the separate enable gate.
## Processor performance telemetry correction

reference host's `PROCESSOR_POWER_INFORMATION.CurrentMhz`, PDH `Processor Frequency`, and `% of Maximum Frequency` do not provide a useful dynamic performance trace on this CPU: they can remain near 3401 MHz / 100% while core count, package power, and actual boost behavior change materially. The primary dashboard performance signal is therefore Windows PDH `% Processor Performance`.

- `CPU PERFORMANCE` is plotted as percent of nominal/guaranteed processor performance and may exceed 100% under boost.
- A GHz-equivalent value may be shown as secondary context (`nominal MHz × Processor Performance / 100`); it is not presented as a literal hardware clock measurement.
- Available/delivered capacity uses each unparked logical processor's `% Processor Performance` first, falling back to `% of Maximum Frequency` only if the real performance counter is unavailable.
- `% Processor Utility` remains the work/demand signal. Performance and utility are deliberately separate dimensions.

## Measured PowerFlow operating modes

The product modes are operating envelopes, not aliases for three Windows plan names. Each mode combines a Windows plan with a measured readiness floor, energy/performance preference, and boost behavior. All modified processor-policy values are snapshotted and restored on mode change, Auto, failure, or shutdown.

| PowerFlow mode | Windows base plan | Core floor | EPP | Boost mode | Intent |
| --- | --- | ---: | ---: | --- | --- |
| Saver | Power Saver | 10% | 60 | Disabled (0) | Low floor; make demand prove itself before capacity/boost is granted. |
| Balanced Efficient (`BAL-E`) | Balanced | 25% | 35 | Efficient Enabled (3) | Efficiency-biased equilibrium; modest readiness while retaining burst performance. |
| Balanced Performance (`BAL-P`) | Balanced | 50% | 20 | Efficient Enabled (3) | Mid-readiness bridge between BAL-E and Performance. |
| Performance | Balanced | 75% | 10 | Aggressive (2) | High readiness without forcing High Performance's 100% minimum processor state. |
| Ultra | High Performance | 100% | 10 | Aggressive (2) | Fully pre-armed; all cores ready and High Performance floor semantics accepted. |

Qualification measurements on reference host under the contemporaneous background workload:

- Saver/Aggressive baseline: about 127.6% Processor Performance and 82.3 W. With boost disabled: about 99.2% and 46.8 W. This is why Saver explicitly disables boost.
- Windows Balanced default: about 128.2%, 101.9 W, 16 awake physical cores. PowerFlow Balanced candidate: about 127.6%, 80.7 W, roughly 4 awake cores during the calibration sample.
- PowerFlow Performance candidate: about 128.2%, 99.8 W, roughly 12 awake cores.
- Compiled profile live qualification: Saver ~99.3% / 50.1 W / 6 cores; Balanced ~126.1% / 87.4 W / 5.3 cores; Performance ~121.9% / 97.8 W / 12 cores; Ultra ~122.7% / 109.6 W / 16 cores. These are observations, not fixed targets; workload changes the absolute values.

The readiness ladder is intentional: Saver < BAL-E < BAL-P < Performance < Ultra. Saver and both Balanced modes remain dynamic; Performance and Ultra increasingly pay idle/readiness cost to reduce capacity wake-up latency.
## Efficiency Compare and control-layer status

### Control layers currently owned by PowerFlow

The measured manual Saver / BAL-E / BAL-P / Performance / Ultra profiles currently apply four concrete processor-policy layers:

1. Windows power-plan state (Saver, Balanced, or High Performance as the substrate).
2. Core-parking minimum (`CPMINCORES`) as the readiness/capacity floor.
3. Energy-performance preference (`PERFEPP`).
4. Processor performance boost mode (`PERFBOOSTMODE`).

`CPMINCORES` is causally qualified as the graduated capacity actuator. `PERFBOOSTMODE` is causally qualified as a strong performance/power actuator (Saver Aggressive vs Disabled produced a large measured performance/watt difference). `PERFEPP` is writable and restored exactly, but the earlier isolated sweep on the then-current path did not show a material response; do not treat EPP as an independently qualified actuator until it is re-characterized under the current performance-control path.

PowerFlow does **not** currently write the deeper Windows processor-control family: minimum/maximum processor state, autonomous-mode ownership, performance increase/decrease thresholds, increase/decrease timing/policy, boost policy percentage, core concurrency/headroom, core increase/decrease timing/policy, or related scheduler/performance-state knobs. Those remain Windows-owned. The goal is not to own every hidden setting; a new layer should only be adopted after an isolated reversible characterization proves that it adds useful control beyond core readiness + boost without destabilizing Saver.

### Remaining Auto work

Auto is not yet the final multi-layer controller. The intended control order is:

1. Core-floor / delivered-capacity loop as the primary continuous actuator.
2. Boost availability/mode as the next performance-side actuator, with mode-envelope constraints.
3. EPP or deeper performance-state timing/threshold controls only after isolated qualification.
4. A top-level `POWERFLOW | WINDOWS` ownership switch so Windows-native plans can be selected as a true monitor-only baseline with all PowerFlow writes and plan switching disabled.

### Efficiency Compare

The Compare experiment is app-owned so recording can continue while the dashboard is closed. It supports 5-minute, 30-minute, and 5-hour Baseline/After phases. Recording itself performs no policy mutation.

Generic Windows telemetry cannot truthfully report application jobs completed. Compare therefore uses a named **CPU work index** derived from `DemandPressureTelemetry.DemandPercent`. That signal is Windows Processor Utility summed over logical processors and normalized to the machine's nominal full-CPU capacity. Integrating `DemandPercent / 100` over time yields **nominal full-CPU equivalent minutes**. It is a consistent CPU-service proxy, not an application throughput counter.

Package energy is the trapezoidal integral of measured package watts over the same intervals, reported in Wh. Intervals larger than 15 seconds are excluded rather than integrated across suspend or telemetry loss; each phase reports coverage.

For the After phase, equivalent-work baseline energy is:

`baseline Wh / baseline work-min * after work-min`

Estimated energy avoided is that equivalent-work baseline energy minus actual After Wh. Compare also reports work/Wh, CPU work rate, average/peak compute pressure, and average processor queue length so a lower-energy result is not called a win if service rate or responsiveness materially regresses.

Application-specific work counters may later replace the generic CPU work index for known workloads when a trustworthy throughput signal exists.
### Task-Manager-aligned CPU speed

PowerFlow now separates two processor-performance meanings instead of mixing them:

- **aggregate CPU speed/readout:** Windows PDH `\Processor Information(_Total)\% Processor Performance` multiplied by the processor nominal/base MHz. This is the Task-Manager-style machine-level speed context shown in tooltips/readouts;
- **capacity modeling:** per-logical-processor `% Processor Performance`, retained because delivered capacity depends on which logical processors are awake and how much performance each is delivering.

On reference host, the aggregate counter was observed around 129-130% while the nominal/base value remained 3401 MHz, yielding roughly 4.4 GHz machine-level speed context. `Processor Frequency` and `% of Maximum Frequency` remain flat on this platform and are not used as the primary dynamic speed signal.

### Telemetry cadence controls

Rich telemetry cadence is user-configurable and takes effect at runtime without replacing the telemetry source:

- visible/dashboard interval: 250-5000 ms;
- background/tray interval: 500-10000 ms.

The default remains conservative. The recorder restarts only its cadence timer when the configured interval changes; ownership/history remain app-level so Compare and background continuity are preserved.

### Dense live instrument

The live timeline uses three visual rows while retaining four aligned logical data series:

1. Compute Pressure.
2. Package Power + CPU Performance overlay.
3. Capacity / Cores.

CPU Performance no longer consumes an entire row on reference host because it is often near-flat during sustained boost. Power remains the primary scale in the middle row; CPU Performance is a secondary percent overlay with its own labeled focus range and explicit headroom. Both share the same time axis but not the same numeric scale.

### Balanced split calibration

The product now distinguishes two Balanced envelopes on the same Windows Balanced substrate:

- **BAL-E:** core floor 25%, EPP 35, efficient boost mode 3.
- **BAL-P:** core floor 50%, EPP 20, efficient boost mode 3.

A same-plan exploratory sweep under the contemporaneous workload observed approximately 72.7 W / 125.5% performance for BAL-E settings, 86.4 W / 128.6% for BAL-P settings, and 91.8 W / 128.4% for Performance settings. Because that sweep was intentionally performed without switching the underlying Windows plan, it is useful only as evidence that the midpoint settings create a graduated readiness/power ladder; it is not the final product-mode watt characterization. Absolute wattage remains workload-dependent.
