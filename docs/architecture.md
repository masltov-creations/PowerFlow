# PowerFlow architecture

PowerFlow is a single Windows desktop process with three main layers: telemetry, policy, and actuation.

## Runtime flow

```text
Windows telemetry
      |
      v
recent observation history
      |
      v
adaptive envelope + app importance
      |
      v
model zone (Eco / Efficient / Responsive / Boost)
      |
      v
PowerFlow profile (SAVER / BAL-E / BAL-P / PERF)
      |
      v
Windows power plan + processor policy
```

Manual profile selection bypasses the automatic zone-to-profile choice and applies the selected profile directly. ULTRA is manual-only.

## Telemetry

`TelemetryContinuityRecorder` owns a bounded recent history of machine observations. `WindowsSystemMetricsProvider` supplies Windows-side CPU and performance counters, while dashboard telemetry adds package power, clock, active-core, and related information when those sources are available.

The dashboard reads the retained history; telemetry collection is not recreated separately for each view.

## Automatic policy

`AdaptiveGovernorRuntime` evaluates recent observations against the current operating envelope. `EnvelopeGovernor` produces the semantic model zone and decision state. App importance contributes an entitlement that limits or accelerates access to higher performance.

`PowerFlowOperatingProfiles.ForAutoZone` maps the model zone to a PowerFlow profile:

- Eco -> SAVER
- Efficient -> BAL-E
- Responsive -> BAL-P
- Boost -> PERF

`PowerModeProfileRuntime` is the source of truth for the profile that has actually been applied.

The Live dashboard therefore keeps two concepts separate:

- **Model Zone**: the governor's current interpretation of demand.
- **Applied Profile**: the profile currently active on Windows.

## Manual authority

A manual profile selection latches until Auto is selected again. The controller also retains compatibility with older game-latch rules so existing configuration can continue to load safely.

Authority order is:

1. manual profile
2. active game latch from compatible older configuration
3. Auto

## Profiles and actuation

A PowerFlow profile contains both a Windows power-plan choice and a processor-policy overlay. The overlay controls values such as minimum processor state/core floor, energy-performance preference, and boost mode.

`PowerFlowOperatingProfiles` contains the profile definitions. `PowerModeProfileRuntime` applies them, reads back the resulting policy, and exposes the active profile to the rest of the app.

PERF intentionally uses the Windows Balanced plan with a more aggressive processor overlay. ULTRA is the only PowerFlow profile that uses Windows High Performance.

## Workloads

Current app rules are expressed as `AppImportance`: Low, Normal, or High. Runtime entitlement is derived from that value and used by Auto when evaluating higher-performance zones.

The configuration reader remains tolerant of fields written by older versions. Unsupported service-policy and tuning fields do not create a second actuation path.

## Baseline

`MachineBaselineComparison.Standard` defines the seven fixed baseline legs. `PowerFlowMachineBaselineHost` applies each leg and snapshots the Windows plan and processor policy before the run so the original state can be restored afterward.

`MachineBaselineRunner` gathers idle and synthetic-load measurements. The synthetic workload runs at 1, 2, 4, 8, and 16 workers to produce a throughput curve for each profile.

The baseline architecture also requires a core-concentration characterization path. It must isolate core availability from EPP, boost mode, Windows plan, and workload shape, then repeat the worker-count curve across machine-relative low/intermediate/full core-availability points. Configured parking floor and observed core residency are separate data: conclusions about parking require actual awake/parked physical/logical-core telemetry during each sample.

The characterization sample model must preserve throughput, package power, effective/per-core frequency, processor-performance percentage, observed awake/parked cores, and reliable temperature telemetry. Reliable voltage/VID telemetry should be captured when available but must remain nullable rather than inferred. Policy and scheduling constraints used for each point must be persisted with the sample.

Analysis produces a machine-specific core-scaling map covering single-thread boost, partial-load concentration, scaling knee, and thermal/power saturation. Deterministic affinity or CPU-set restriction may be used to establish causality during characterization, but it is not automatically a runtime actuation mechanism. Adaptive runtime consumption requires repeatable qualified evidence and should prefer normal Windows parking/scheduling controls.

## UI

`MainWindow` hosts four sections:

- Live
- Workloads
- Baseline
- Settings

`PerformanceTimelineControl` renders the Live history. The same runtime state is reused as the window changes between compact, expanded, and full-screen layouts.

The tray is another entry point into the same running process rather than a separate controller or telemetry service.

## Storage

Per-user configuration is stored in `%LOCALAPPDATA%\PowerFlow\config.json`.

Older configuration fields may still be deserialized so upgrades do not break existing installations, but current behavior is driven by the features described above.

## System boundary

PowerFlow writes only Windows power-plan and processor-policy settings. BIOS, voltage, fan, and firmware controls are outside the application.
