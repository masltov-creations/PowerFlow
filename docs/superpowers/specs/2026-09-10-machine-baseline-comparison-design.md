# Machine Baseline Comparison Design

## Goal
Provide one obvious **BASELINE MACHINE** action that characterizes reference-host across Windows-native and PowerFlow operating modes and turns the evidence into overlaid curves, knee points, idle-cost measurements, and a plain recommendation.

## Run sequence
A standard run contains six five-minute legs in this fixed order:
1. Windows Power Saver (native plan, no PowerFlow processor-policy overlay)
2. Windows Balanced (native plan, no PowerFlow processor-policy overlay)
3. PowerFlow BAL-E
4. PowerFlow BAL-P
5. PowerFlow Performance
6. PowerFlow Auto (current governor, no manual overlay)

Each leg is exactly five minutes by default: 15 s settle, 90 s idle observation, then five 39 s synthetic CPU points at 1/2/4/8/16 workers. The benchmark never changes modes itself.

## Evidence
For every mode retain the exact policy signature actually observed. Idle evidence includes average/p95 package watts, average CPU performance/speed, average pressure/queue, and energy. Bench evidence includes throughput, watts, speed, processor performance, throughput/watt, knee, best-efficiency worker count, and max-throughput worker count.

## Recommendation
Do not use an opaque weighted score. Report:
- lowest idle-power mode;
- best 1-thread throughput;
- best throughput/watt point;
- highest absolute throughput;
- balanced recommendation = among modes that achieve at least 95% of the global maximum throughput, choose the one with the lowest measured idle watts (fall back to best efficiency if idle evidence is unavailable).
Explain the recommendation and quantify the idle-watt delta versus native Windows Balanced.

## Safety
The runner snapshots the pre-run PowerFlow/manual state and processor policy, restores between legs as required, and restores the original state in `finally` on completion, cancellation, or fault. Native Windows legs are latched to the requested Windows plan so Auto cannot interfere. Auto is deliberately the final leg. The runner never invokes Ultra in the standard baseline because the requested comparison set does not include it.

## Persistence and UI
Persist a bounded history of completed baseline runs. The Profile page gets one primary BASELINE MACHINE button, progress/cancel controls, an overlaid throughput chart, selectable overlay metric (Throughput / Throughput per Watt / Package Power), idle-power comparison, knee/result table, and recommendation cards. Existing single-current-mode profiling remains available as an advanced secondary action.