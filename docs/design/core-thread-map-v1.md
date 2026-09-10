# CPU core history v2

The fourth PowerFlow timeline lane is a compact CPU-core history, not a current-state topology diagram and not a generic line chart.

- Time runs left to right. One visual column represents one telemetry time slice.
- Within every time slice, all physical cores are stacked vertically in stable core-index order. On the target machine this is 16 rows per slice.
- Each core cell aggregates its logical siblings truthfully: all parked => parked; any awake => awake; any logical sibling at or above the active threshold => active.
- Cell brightness follows measured logical-processor utilization; Missing utilization must never be promoted to Active.
- The cells are intentionally dense and nearly gapless so the lane reads as a histogram/heatmap over time rather than a collection of floating boxes.
- There is no separate aggregate core line. The stacked history is the primary visualization.
- CPU pressure, package power, and effective clock remain graceful continuous shape-preserving curves. Isolated single-sample figures are not rendered as round-cap dots.
- Current summary text reports awake physical cores and active physical cores; tooltip text explains parked/awake/active semantics.
