# PowerFlow Core / Thread Map v1

## Product intent

Core and thread state is discrete occupancy, not a continuous scalar. PowerFlow must not render it as a fourth generic telemetry curve. The timeline keeps graceful continuous curves for CPU pressure, package power, and effective clock; the fourth lane becomes a compact live topology map with a subordinate history strip.

## Current-state encoding

- One visual column represents one physical core.
- Each small rounded square in that column represents one logical processor (SMT sibling when present).
- Logical processors are ordered by logical processor index within each physical core.
- Active: unparked and measured logical-processor utilization is at least 5 percent.
- Awake / idle: unparked but measured utilization is below 5 percent, or utilization is unavailable.
- Parked: Windows Parking Status reports parked.
- Missing utilization must never be promoted to Active.
- The side summary reports awake physical cores plus active and parked logical threads.

## History encoding

The existing aggregate awake-physical-core samples remain useful as trajectory. They are rendered only as a thin, low-emphasis, smooth history strip beneath/behind the live square matrix. It must not compete with the square map for attention.

## Visual hierarchy

1. Graceful continuous telemetry curves: CPU pressure, package power, effective clock.
2. Live core/thread square matrix.
3. Quiet aggregate core history strip.
4. Policy and event context behind telemetry; detailed prose remains in hover/inspection.

No point markers, dotted core traces, or in-lane core labels are permitted.

## Data contract

Windows rich telemetry carries, for each logical processor: logical processor index, physical core index, parking state, and optional utilization percent. The governor's OperatingObservation remains aggregate-only; per-thread topology is a presentation/telemetry concern and is not added to the control-plane model.

## Acceptance

- On a 16C/32T system, the live map shows 16 physical-core columns and up to two thread squares per column.
- Active, awake-idle, and parked states are visually distinct.
- Per-thread state originates from Windows counters; the UI does not synthesize topology from aggregate counts.
- The fourth lane contains no CoresTracePath generic graph.
- The other three lanes remain cubic, round-capped smooth traces.
- Compact presentation remains legible at 760x440 without overlapping text.
- Missing per-thread telemetry degrades to aggregate summary/history without fabricated square states.
