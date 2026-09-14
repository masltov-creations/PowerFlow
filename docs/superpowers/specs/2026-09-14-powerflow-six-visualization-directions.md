# PowerFlow Six Visualization Directions

**Design source:** Operator-provided proposal, 14 September 2026. This active spec incorporates that proposal into the public PowerFlow tree for implementation planning.

## Recommendation

Ship **Signal Desk** as the default Live view. Add **Core Atlas** as inspectable core detail. Develop **Parallel Worlds** as PowerFlow's signature explanation of active-versus-shadow governance. Add **Operating Orbit** and **Tension Lens** as focused analysis views. Keep **Silicon Weather** opt-in and performance-gated.

All six presentations live inside the existing persistent PowerFlow shell. They reuse retained data; they are not separate telemetry collectors or independent dashboards. Visualization work must not alter governor policy, enable ULTRA in Auto, add sensor polling, predict energy savings, replace the persistent shell, or imply measurements the sources do not provide.

## Verified product context

- Current product references are `README.md`, `docs/product.md`, and `docs/architecture.md`.
- Model Zone and Applied Profile are separate states. Auto maps Eco/Efficient/Responsive/Boost to SAVER/BAL-E/BAL-P/PERF. ULTRA remains manual-only.
- `TelemetryContinuityRecorder` supplies bounded shared history and owns retention.
- `GovernorPressureProjection` supplies rich pressure when available, with labeled CPU-busy fallback.
- `TensionShadowRuntime` is advisory and must never actuate policy.
- `PowerModeProfileRuntime` is authority for confirmed applied profile.
- `PerformanceTimelineControl` is the current history renderer; `ShellMotionCoordinator` owns shell transitions.
- `docs/design/core-thread-map-v1.md` defines grouped core-state history, not identity-preserving per-core history.
- Detailed core telemetry has processor-group limitations; package power and rich counters are optional.
- Existing shell dimensions and density logic remain authoritative: Hover 320x219, Compact 760x440, Expanded 1280x800, Workspace 1360x860, plus Full Screen.

## Shared visual and behavior contract

1. Applied profile and authority remain visible in every mode.
2. Model zone is independently labeled and never recolors the applied badge by itself.
3. Unknown, stale, unsupported, missing, invalid, and measured zero remain distinguishable.
4. Detailed views reuse one selected timestamp and one selected physical core where applicable.
5. Visualization selection is local UI state only and cannot choose a profile, change tension, or write policy.
6. Discrepancy between Model and Applied is not automatically failure. Show the recorded reason or `Reason unavailable`; never infer a reason from chart shape.
7. Use Segoe UI Variable when available with Segoe UI fallback, 12 px minimum labels and 14 px normal controls, Windows text scaling, light/dark/high-contrast/reduced-motion treatments, and a non-color cue for every state encoding.

## Shared presentation snapshot

Create immutable presentation snapshots outside the UI thread with stable sequence/monotonic/display timestamps; CPU busy; pressure value/source/range; nullable package watts/effective clock with source/units/quality; topology/core IDs and states; Active/Awake-idle/Parked/Unknown counts; independently timestamped model/applied/authority/reason; active/shadow sequence + configuration revision; and per-signal Valid/Stale/Unsupported/Missing/Invalid quality.

Never merge different sequence IDs into one apparently simultaneous decision. Expose asynchronous ages/alignment tolerance. Break paths on resume/sequence gaps. The recorder owns retention; views project only visible intervals, cancel obsolete work, and bound output by viewport/history limits.

## Performance requirements

These are acceptance targets until measured:

- no extra telemetry polling, sensor handles, or governor evaluation from a view;
- retain current acquisition cadence;
- data-driven/static by default; visual interpolation at most 180 ms without invented readings;
- opt-in cinematic motion at most 30 FPS, one outstanding render, stopped when inactive/minimized/occluded where detectable, and disabled for reduced motion;
- projection CPU p95 <= 2 ms and UI submission p95 <= 4 ms on the qualification machine at Expanded size;
- additional whole-machine CPU over Signal Desk <= 0.25 percentage points over matched 10-minute capture and sustained GPU-engine increase <= 2 percentage points;
- extra retained memory per active visualization <= 16 MB after warmup and no monotonic growth in a 30-minute run;
- reduce geometry/motion first when budgets are missed; never reduce shared telemetry cadence silently;
- no per-cell XAML controls for history; batch paths/drawing primitives.

## 01 - Signal Desk

**Question:** what changed, when, and what did PowerFlow do?

Four aligned lanes show compute pressure, package watts, effective clock, and grouped physical-core state counts. Separate thin step lanes show confirmed Applied Profile and Model Zone. One cursor selects a timestamp across every lane. Core counts preserve Active/Awake-idle/Parked and add visibly distinct Unknown where required; they never imply stable core identity through time.

Use explicit lane units/domains, stable padded watts/clock domains, gaps for missing samples, compact availability labels for unsupported signals, shared keyboard/pointer/touch selection, decision markers with recorded explanation, Compact four-lane density, and Expanded decision detail. Extend the existing timeline path rather than rebuilding retained history.

Acceptance: one selected timestamp agrees across lanes; model change cannot change applied state until confirmed; unavailable watts never becomes zero; resizing preserves selection and does not recreate telemetry; core rendering remains bounded.

## 02 - Core Atlas

**Question:** how is work distributed across the CPU right now?

Render stable physical-core tiles with logical-thread marks, ordered by documented topology ID. Group by processor group/die only when source data proves the relationship; otherwise label `Schematic order`. Active, Awake-idle, Parked, and Unknown each require a second visual cue. Selection opens logical members and available current readings.

Do not sort the default map by utilization. When reliable topology is unavailable, replace the map with aggregate counts and an exact coverage explanation. Do not create historical identity data as part of this slice.

Acceptance fixtures cover 4/8/16/24/64 cores where supported, SMT on/off, missing members, and unsupported multi-group systems. Known counts plus Unknown equal total cores. A parked core never gets an inferred zero clock. No fake die geometry or core temperature.

## 03 - Tension Lens

**Question:** how does active governor behavior differ from proposed shadow behavior?

This is a policy diagram, not hardware capacity. Use a restrained active solid/shadow dashed envelope only when at least three explicitly mapped comparable governor dimensions exist. Candidate dimensions are capacity allowance, response readiness, hold duration, and recovery behavior. Every enabled axis must have a documented adapter to actual governor configuration, original units, and direction. Below three comparable dimensions, render aligned interval bars instead of a polygon.

Never derive an axis from profile rank, EPP, or core-floor percentage unless the governor contract explicitly defines that relationship. Shadow tension updates `TensionShadowRuntime` only; applied authority does not morph to shadow state.

Acceptance uses golden mapping fixtures, verifies omitted dimensions have reasons, proves tension edits cannot actuate, preserves manual ULTRA authority, and never fabricates missing dimensions.

## 04 - Operating Orbit

**Question:** what operating region did the machine occupy, and how did it get there?

Plot compute pressure on X and measured package watts on Y. Recent history is a chronological fading trajectory with an unmistakable newest point. Use declared pressure scale and labeled CPU-busy fallback. Age controls opacity, sample markers preserve time ordering, selected points expose exact values/source/profile, and visible-window domains remain stable.

If package power is unavailable, show `Package power unavailable` and offer Signal Desk. Never estimate watts or silently replace Y with frequency. Do not draw efficiency regions from live busy percentage; baseline overlays require comparable measured workload dimensions.

Acceptance: missing/stale watts break paths; loops preserve chronology; hit testing stays correct through resize/text scaling; replay never interpolates across long gaps.

## 05 - Silicon Weather

**Question:** what does recent machine activity look like as a living landscape?

Render an orthographic 2.5D landscape of grouped core-state history. Time runs horizontally, grouped state counts run in depth, and height is categorical only. Always label the data as `Core-state counts`; never imply individual-core migration or a physical silicon floorplan. A decision ribbon anchors the scene to actual profile changes.

Provide top-down inspection with identical counts, exact pinned time slices, static/reduced-motion treatment, Compact flat ribbon, Expanded/Workspace relief, and deliberate camera easing only on direct user input. Cap time columns at 160 and count bands at 64, with explicit aggregation labels for larger topologies. Start in the existing rendering layer; no WebView/game engine unless a measured spike proves it necessary.

Acceptance: top-down/relief counts match exactly, Unknown remains visible, exact selection survives occlusion, geometry stays bounded, and static rendering meets performance gates before motion is enabled.

## 06 - Parallel Worlds

**Question:** where would a different governor have made different decisions?

Render two synchronized profile ribbons over one time axis: confirmed actual Applied Profile above, advisory shadow recommendation below. Use four normal profile levels and a fifth actual level for manual ULTRA when present. Disagreement bands connect only comparable aligned observations. Detail shows observation timestamp, actual authority/profile, active recommendation, shadow recommendation, and recorded reasons.

The shadow sees observations produced under actual policy; it is not an independently simulated machine. Report recommendation differences, transition counts, and dwell duration under observed input only. Never report shadow watts, saved energy, temperature, latency, speedup, or inferred measured benefit.

Join only on source observation sequence plus active/shadow configuration revision. Shadow-config changes start a labeled segment or explicitly re-evaluate bounded history; never stitch revisions invisibly. Manual/game-authority intervals get neutral authority annotation. Missing/mismatched sequences are gaps.

Acceptance: shadow-only changes result in zero policy writes; actual profile remains correct through failed/delayed actuation, manual ULTRA, and game authority; different observation IDs never pair; comparison stats exclude missing/unaligned intervals and state their denominator.

## Implementation sequence and release gates

1. Inspect only existing snapshot models, timeline code, shell density/motion integration, shadow output, and relevant tests. Confirm units, timestamps, reasons, and topology coverage.
2. Implement pure shared presentation/quality/selection contracts with semantic fixtures before new rendering.
3. Deliver Signal Desk and Core Atlas behind a Live visualization choice, reusing shell state and acquisition.
4. Add Parallel Worlds only after sequence-alignment and counterfactual isolation tests pass.
5. Add Operating Orbit; add Tension Lens only after its semantic adapter is qualified.
6. Prototype Silicon Weather behind a feature flag and enable opt-in only after CPU/GPU/memory/accessibility gates pass.

Required fixtures include quiet idle, short burst, sustained demand, cooldown, missing watts, invalid clock, missing core members, unknown topology, unsupported processor-group coverage, telemetry interruption, clock jump, rapid shell resize, reduced motion, high contrast, 200% text scaling, manual ULTRA, delayed/failed actuation, shadow configuration changes, and missing shadow segments.

Verification includes projection correctness, bounded resources, shared-cursor alignment, no visualization-triggered actuation, and existing regressions. Performance comparisons use the same recorded input, viewport, release build, and machine and record measurement method/noise.

## Active-desktop rule

Native PowerFlow launch, capture, resizing, or synthetic-input validation requires explicit operator authorization for that live action. Complete headless source checks, tests, and builds first. Browser/concept previews do not validate native WinUI behavior.