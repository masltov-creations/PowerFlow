# PowerFlow — Adaptive Harmonic Ring implementation plan
Status: **Implementation branch / unqualified** · 2026-10-23
Base: `master` at `621fc26cf863d0e5e3925cb8ef9f9fc7e8209f8a`
Scope: **new opt-in visualization**, NOT a replacement for the current Live view or power governor.

## Product experience

One coherent instrument, three presentations driven by *the same presentation snapshot and renderer*:

1. **Hover Orb**: visually circular (target diameter ~240–320 logical px, resizable), minimal unobtrusive readouts: compute pressure and trend (building / steady / ebbing), measured package watts if present, measured effective clock if present, awake/total cores if known, applied profile and data freshness. Ring wave is a trace of bounded recent pressure history, not a fake perpetual waveform.
2. **Adaptive Card**: enlargement progressively reveals labeled individual values and two short sparklines; continuous ring identity/position retained, with a stable geometry morph and no duplicated UI trees during transition. Details appear only when usable width/height has persisted beyond hysteresis boundaries.
3. **Expanded Dashboard**: same ring, measured charts with independent units and synchronized timestamps, details for pressure, processor busy if actually available, measured power (W), effective clock (MHz/GHz), active/awake/parked/unknown core counts, applied state/model/authority, and recent changes with recorded reasons. Long histories and selection remain in the recorder-owned timeline.

An interactive pin affordance maintains a compact orb. Drag moves the window. Resizing the orb increases its diameter without changing the governing power mode. Deliberate expand/mode selection enters the adaptive/card path. The app never forces a mode change simply because a telemetry value changes.

### Truthful visual grammar

| Evidence | Encoding | Semantic restriction |
| --- | --- | --- |
| `OperatingObservation.CpuPressurePercent` | Cyan/blue pressure ring radius/amplitude and historical crest position | It is **compute pressure**, not measured CPU utilization. A 60-second circular timeline has an explicit beginning/end tick, so it is not mistaken for a periodic signal. |
| Recent change in pressure | Crest direction and short label **building / ebbing / steady** | Trend must be based on sufficient finite samples over known timestamps; below threshold or sparse data => steady/unknown. |
| Actual CPU busy/utilization, only if reliable independent source exists | Subtle wave texture/density | The current `OperatingObservation` does **not** expose CPU busy: until an existing retained source is qualified, label Usage unavailable; DO NOT alias pressure or active-core count to usage. No new sensor polling. |
| `PackageWatts` | Amber energy arc and `W` label | Actual watts only, absent/stale => gray dashed/unavailable, never zero or fabricated watts. Aesthetic brightness is a clearly relative visual transform, not a power efficiency claim. |
| `EffectiveClockMhz` | Central `GHz` text and narrow frequency halo | Source is measured effective clock; never replace with processor performance percentage, frequency maximum, or inferred clock. |
| `ActiveCores`/`TotalCores` | Finite orbital ticks with lit vs unknown state | Existing `OperatingObservation` calls these active but lacks individual identity; use aggregate `awake/total` language unless core-state classification proves 'executing'. A zero must not be invented or presented as missing. |
| Confirmed applied profile | Small static badge in center | Distinct from current model zone and from simulation/shadow proposal. |
| Stale/missing/unsupported/invalid | Explicit dash/label and muted ring segment | Never smoothly interpolate through long gaps or relabel stale as current. |

**No inference from ring color or flourish can imply precise causal flows, measured utilization, efficiency, work completion, temperature, or savings.** Pulse/wave/ribbon imagery is a visual mnemonic for historical change, not an electrical energy Sankey.

## Integration points found in the current source

- `App.xaml.cs` owns `TelemetryContinuityRecorder` and controller updates.
- `Dashboard/MainWindow.xaml.cs` owns `ShellMotionCoordinator`, `ShellTransitionGeometry`, `ShellResponsiveDensity`, `DispatcherQueueTimer`, drag and resize state. **Do not create a second HWND geometry authority.**
- `Dashboard/PerformanceTimelineControl.xaml.cs` owns current timeline drawing, with `PerformanceTimelineProjection` and `CurrentTelemetryProjection` adapters.
- Current `OperatingObservation` contains pressure, package watts, effective MHz, processor-performance percent, aggregate active/total cores, zone, actor and decision; notably **no independently evidenced CPU-busy field**.
- Current shell presets: Glance 320×219, Compact 760×440, Expanded 1280×800, Workspace 1360×860. The circular 1:1 hover form requires a distinct **opt-in presentation profile** layered onto the current shell authority and tested at multiple DPI scales; do not globally change legacy Glance yet.

## Work slices and dependencies

### S0 — Freeze baseline, source audit, red tests (shared gate)
- [ ] Inspect local installed version and repo status; snapshot safe rollback/installed EXE/config hashes without changing policy.
- [ ] Confirm exact retained telemetry ages, source units, core-state semantics, existing window geometry and input handlers.
- [ ] Establish standalone feature flag; assert default UI and power plan remain unchanged.
- [ ] Add RED fixtures for stale/missing data, pressure vs CPU busy, zero vs missing, out-of-order timestamps, no policy writes.
- [ ] Assign disjoint paths/owners. All agents use *only* an approved project identity, explicit model and reasoning, isolated branches/worktrees, and no popups.

### S1 — Pure presentation/data projector (agent lane A; parallel after S0)
Files: `Dashboard/HarmonicRing/HarmonicRingSnapshot.cs`, `HarmonicRingProjection.cs`, `HarmonicRingTrend.cs` + tests.
- [ ] One immutable, sequence-stamped ring snapshot with per-metric values, units, timestamp/age and availability.
- [ ] Bounded fixed-size history points from existing `OperatingObservation`; no acquisition, retention or controller changes.
- [ ] Pressure trend with deadband and minimum samples/time; break long gaps and avoid false smoothing.
- [ ] Keep applied profile, model zone and authority independent.
- [ ] Tests: missing/stale/invalid, frequent updates, pressure rise/fall, flat noise, reordered samples, large sample bursts.

### S2 — Geometry, harmonic waveform and compositor (agent lane B; parallel after S0)
Files: `Dashboard/HarmonicRing/HarmonicRingGeometry.cs`, `HarmonicRingControl.xaml*`, renderer/brush helper + tests.
- [ ] Deterministic 60-second radial history trace (bounded 64–128 segments, identifiable 'now' seam) and finite geometry.
- [ ] Batch a small number of paths/primitives; cache static rings/ticks/gradients by size, DPI, theme and quality.
- [ ] Warm watts band, measured MHz center, core ticks; missing values visually distinct.
- [ ] Compositor-friendly `SpriteVisual`/cached geometry where supported; no WebView, shader compilation or frame-by-frame full XAML tree regeneration.
- [ ] 0 FPS at rest; optionally up to 30 FPS during visible/interacting motion; one outstanding frame; stop offscreen/minimized/occluded when detectable; reduced motion static.
- [ ] Test geometry bounds, min/max resizing, DPI rounding, finite values, contrast, and animation stop.

### S3 — Adaptive layout and shell mechanics (agent lane C; parallel with S1/S2, no shared edits)
Files: `Dashboard/HarmonicRing/HarmonicRingLayout.cs`, view-selection state/policy + tests. Shell integration in S4.
- [ ] Progressively disclose from circle to supporting cards to expanded dashboard using hysteresis in BOTH dimensions.
- [ ] Preserve same ring visual and selection; don't instantiate duplicate rings or controls during the morph.
- [ ] Separate user dragging vs resizing and preserve cursor anchor where reasonable.
- [ ] All target bounds use existing `ShellTransitionGeometry` / work-area clamp; preserve center during free resize and clamp **only** to prevent growth clipping.
- [ ] Test all edges/corners, negative monitor origins, mixed DPI, tiny work area, taskbar changes, rapid retargets, returning from fullscreen.

### S4 — Wiring/integration (single integrator, depends on S1–S3)
- [ ] Add `Harmonic Ring (Preview)` to Live visualization selector with persisted UI-only preference; legacy chart default unchanged.
- [ ] Drive ring using existing recorder history and existing controller/applied-state stream; no telemetry timers or power-policy writes.
- [ ] Opt-in circular hover vs pinned square dashboard integration through **one** shell geometry authority.
- [ ] Make square HWND window hit-testing transparent outside orb *only if* WinUI/DWM safe; otherwise ship a minimal square borderless surface with circular visual until hit-testing can be qualified—don't block desktop input invisibly.
- [ ] Morph styles via opacity/transform only; avoid relayout storms, visual pop, and nested scroll/overflow.
- [ ] Preserve keyboard navigation, pointer drag/grips, high contrast, text scaling, reduced motion and screen-reader semantic equivalents.

### S5 — Parallel quality lanes (agent lanes D/E after S4 candidate)
- [ ] **D: Data correctness**—fixture replay, source gaps, time alignment, stable units/scales, independent applied/model/authority checks.
- [ ] **E: Perf/reliability**—record PowerFlow CPU p50/p95, GPU engine, `dwm.exe` CPU/GPU, UI frames, allocations/working set at idle, movement, continuous resize and busy workloads.
- [ ] Comparative acceptance from same hardware, same data and same visibility: zero new polling, projection CPU p95 <= 2 ms, UI submission p95 <= 4 ms, additional whole-machine CPU <= +0.25 percentage points, GPU-engine <= +2 percentage points, ring memory <= +16 MB after warmup, no growth in a 30-minute run. These are **targets, not observed results**.
- [ ] Verify stop-scheduling on minimize, obscuration if detectable, reduced motion, and minimized/resumed state.

### S6 — Native E2E / release gate (integrator, exclusive)
- [ ] With operator-authorized UI testing, run visual screenshots and HWND/control bounds for 125/150/200% DPI across circle sizes and every transition.
- [ ] Slow and rapid corner/edge drag/resize; expand/collapse while telemetry changes; multi-monitor and work area clamps; restore original position and mode.
- [ ] No clipped labels/controls, disappearing widgets, duplicate controls, unrequested jumps, accidental window input capture, popup command windows, or resize feedback oscillation.
- [ ] All tests and Release build green, zero warnings, privacy/artifact gates; validate installed-copy update/uninstall/config preservation and durable rollback separately.
- [ ] Only deploy to Zippy after all above gates pass; preserve current installed PowerFlow until then.

## Parallel agent coordination

- **Antigraviti**: candidate renderer/layout implementation and visual/performance E2E; dispatch only after verifying its real tool/channel + WinUI repo access. **No Antigraviti integration currently verified here.**
- **GPT-6 Luna via guarded Codex**: projector/test lane and integrator work, model explicitly set to GPT-6 Luna/approved identifier, no model substitutions; dispatch only after PowerFlow project registration and Flight Deck gate/approval are verified.
- Orchestrator keeps one checked-in plan, task owners, commits, short evidence receipts and rollback checkpoints. Do not parallel-edit MainWindow or shell state; single owner for integration.
- The bridge's current approved-project catalog does not include `powerflow` (inspection on 2026-10-23). Treat this as a **dispatch blocker**; do not submit PowerFlow writes through `codex-bridge` or any unrelated project.
- If approval/model/channel unavailable, execute safe independent source/doc changes through a PowerFlow feature PR, state precisely what was not executed, and do not merge or deploy unqualified code.

## Shipping phases

- **Phase 1:** feature-flagged static harmonic ring with truthful values and resize-aware geometry.
- **Phase 2:** interpolated ring motion, measure and cap effects.
- **Phase 3:** seamless circular hover -> adaptive card -> expanded panel, E2E qualified.
- **Phase 4:** release candidate with installer/rollback validation, only after human desktop acceptance.

Done means native WinUI circle exists in the actual product, accurately reflects current telemetry, smoothly morphs to detailed dashboard while remaining responsive, passes automated/native/performance gates, and retains a tested rollback. Concept renders or branch-only code are not acceptance.
