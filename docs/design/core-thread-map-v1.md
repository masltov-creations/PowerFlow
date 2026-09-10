# Core-state timeline v2

## Purpose
The fourth PowerFlow timeline lane must answer, at a glance: how many physical CPU cores were active, awake-but-idle, or parked at each moment, and how that changed alongside CPU pressure, package power, and effective clock.

## Visual contract
- Time remains the horizontal axis shared with the other telemetry lanes.
- Each vertical time slice represents the machine's physical-core count as a compact stack of microtiles.
- Microtiles are grouped by state count, not by physical-core identity: Active at the bottom, Awake-idle above it, Parked above that.
- The vertical extent of each state therefore directly encodes how many physical cores were in that state at that moment.
- The lane uses three persistent Path elements; it must not create one XAML UIElement per cell or per sample.
- Sparse horizontal guides may mark quarter counts, but the lane must not become a dense grid or text field.
- CPU pressure, package power, and effective clock remain graceful curved traces in their own aligned lanes.

## State semantics
A physical core is Parked only when all of its logical processors report parked. An unparked core is Active when at least one unparked logical processor has finite utilization at or above the shared activity threshold. Otherwise it is Awake-idle. Missing utilization must never be promoted to Active.

## Current readout
The lane's current value is a mutually exclusive physical-core summary such as `8 active · 8 awake · 0 parked`. The three numbers should sum to the known physical-core count.

## Performance
The renderer must remain bounded and avoid per-cell XAML controls. Core-state history is retained in the existing bounded continuity history and projected only for the visible timeline window.
