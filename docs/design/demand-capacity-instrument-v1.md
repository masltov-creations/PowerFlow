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
- **Awake saturation** = physical-core load summed across awake cores / awake physical cores.
- **Queue pressure** = runnable queue length / awake physical cores, mapped so one queued runnable thread per awake core equals 100% queue pressure.
- **Observed pressure** = max(machine demand, awake saturation, queue pressure).
- **Driver** = whichever component currently determines observed pressure.

There is no weighted composite. Hover/readout must expose the component values so the pressure number is auditable.

## Core-capacity lane
The existing time-aligned physical-core microtiles remain. Active cells now encode load intensity in four bands (<25%, 25-49%, 50-74%, 75%+), while awake-idle and parked remain distinct. The renderer uses persistent Path geometries, not one XAML element per cell.

Per-core frequency and percent-of-maximum-frequency are retained in telemetry and summarized in the core tooltip for later policy work.

## Safety / promotion gate
Do not use Observed Pressure for automatic actuation until it has been compared against real workloads and its thresholds/timing have been explicitly accepted. The dashboard/dry-run may use the new pressure now; controller actuation remains legacy CPU busy percentage.