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

Initial implementation remains observation/dry-run only. Do not replace the existing power-plan actuator until the graduated policy has been measured against real workloads and each control surface is verified to be reversible and stable on reference-host.
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

Read-only inspection on reference-host confirms Windows exposes graduated processor controls beneath the three named plans. Current AC endpoints are:

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