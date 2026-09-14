# PowerFlow Progressive Disclosure + Continuity Design

Date: 2026-09-08
Status: Approved architecture; implementation planning pending user review
Builds on: `docs/superpowers/specs/2026-09-08-powerflow-design.md`

## Goal

Turn PowerFlow into a concise, progressive-disclosure power instrument that preserves meaningful recent behavior while the dashboard is closed, without violating the product's low-overhead and game-latch constraints.

The visual model must answer, at a glance:

- What state is PowerFlow in now?
- Where has the system been recently?
- Where is it headed if current behavior continues?
- Which policy boundary is relevant right now?
- What will happen next, and why?

The compact view must remain readable. Compactness comes from hierarchy and disclosure, not tiny typography.

## Hard constraints

1. No popup windows, preview launches, cursor movement, UI automation, or other foreground interaction on reference host during implementation unless the user explicitly requests a live acceptance run.
2. PowerFlow remains tray-first and single-process by default.
3. Background overhead is a product gate. Monitoring must not materially erode the power savings PowerFlow creates.
4. Game latch semantics are unchanged: game detection and process lifecycle dominate; low utilization never demotes a game.
5. While a game is latched and no visual surface is open, no rich telemetry polling is permitted solely for visualization.
6. Manual locks retain their existing policy semantics. A hidden dashboard must not restart policy sampling that a latch intentionally suspended.
7. No continuous hidden animation and no 100-250 ms hidden timers.
8. No continuous disk logging for telemetry history.
9. Light, Dark, and Follow Windows themes remain first-class.
10. Reduced Motion remains first-class.
11. No visible primary UI text below 11 px. Normal body/control text remains 12-14 px; key state/telemetry remains larger.
12. Preview/read-only mode remains incapable of changing the real Windows power plan.

## Design principle: one instrument, several depths

PowerFlow should not present the user with a wall of simultaneous cards. The same underlying power-flow model should reveal increasing detail only when the user asks for it.

### Level 0 - Tray glance

The tray hover popup is the smallest expression of PowerFlow:

- current state and AUTO/MANUAL/GAME lock mode;
- CPU, package watts, and GHz when available;
- a short continuity sparkline;
- one concise next-action sentence;
- click anywhere to open the dashboard.

The popup is not a separate mini-application. It reuses the same trajectory/history data and visual vocabulary as the dashboard.

### Level 1 - Compact dashboard

The default dashboard is one coherent instrument rather than several independent cards.

Primary composition:

- a thin top status line: `POWERFLOW · AUTO`, current CPU, package watts, and GHz;
- a central horizontal trajectory field showing recent history, NOW, and the direction of policy pressure;
- Saver, Balanced, and Performance integrated as small mode nodes on that trajectory;
- AUTO / manual override interaction attached directly to the mode nodes rather than repeated in a separate oversized state rail;
- QUIET and PROMOTE policy rails drawn directly through the trajectory field and remaining draggable;
- actual state transitions shown as marks on the trajectory rather than duplicated in a permanent Recent panel.

The visual should make history, present state, and policy intent legible without opening any detail panel.

### Level 2 - Hover lens

Hover reveals exact local context without rearranging the whole dashboard.

Examples:

- hover a telemetry point: timestamp, CPU, watts, GHz, active state;
- hover NOW: state, reason, latch/cooldown, threshold progress, next action;
- hover a transition marker: from-state, to-state, reason, triggering app, timestamp;
- hover QUIET/PROMOTE: threshold, hold time, current elapsed qualification time;
- hover a mode node: manual-lock consequence and current mapping.

The lens is visually rich but spatially local. It should feel like magnifying the instrument, not opening a card wall.

### Level 3 - Click to expand in place

Clicking an object expands that object in the same spatial region while the rest of the dashboard yields space but remains understandable.

Examples:

- click NOW: expand state causality, contributing signals, current rule precedence, and next-action timing;
- click trajectory: expand the time range and expose transition episodes and richer hover inspection;
- click QUIET/PROMOTE: expose threshold and hold-time controls around the selected rail;
- click a transition: expose the complete causal episode;
- click a mode node: expose override controls and selected Windows plan mapping.

The user should never need to mentally map a small object to an unrelated detail pane on the opposite side of the window.

### Level 4 - Rules and Settings

Rules and Settings remain explicit secondary views for durable configuration. They are not part of the default monitoring canvas.

## Default dashboard composition

The visual center of gravity is the trajectory field.

Conceptually:

```text
POWERFLOW · AUTO                              CPU 11% · 62 W · 3.8 GHz

[ SAVER ] -------- recent trajectory -------- ● NOW -------- [ BALANCED ] ---- [ PERFORMANCE ]
                  state bands / transitions / trend

-------- QUIET 21% ----------------------------------------------------------
----------------------------- PROMOTE 44% ----------------------------------

                         context appears only on hover/click
```

This is not a literal wireframe requirement. It defines hierarchy:

1. current state and trajectory first;
2. thresholds overlaid on the same visual behavior;
3. exact detail on demand;
4. durable configuration elsewhere.

## Trajectory field

The trajectory field replaces the current separation between graph, automatic-rules card, decision-pressure card, and Recent card.

It combines:

- CPU history as the primary continuous signal;
- package power as a secondary signal when available;
- current and historical power-state bands;
- transition markers only where state actually changed;
- NOW marker;
- threshold rails;
- threshold-progress / directional pressure;
- optional short projected continuation derived only from current policy progress, never a claim about future CPU load.

### Projection semantics

Projection is intentionally conservative. PowerFlow may show policy trajectory such as:

- `44% threshold, 3.1 / 4.0 s qualified -> approaching Balanced`;
- `below 21% for 17 / 25 s -> returning toward Saver`;
- `Game lock -> Performance held until process exit`.

It must not pretend to predict arbitrary future CPU utilization.

## Transition history

The permanent Recent box is removed from the default dashboard.

History is represented by transition markers on the trajectory. Only actual semantic events create markers:

- successful plan transitions;
- failed transition attempts;
- game/manual latch acquisition/release;
- cooldown start/end where meaningful to explain state.

Unchanged telemetry never becomes an event.

A small affordance such as `3 changes` may expand the episode list when the user wants a textual history.

## Telemetry continuity architecture

### Current problem

Today `DashboardTelemetrySession` is owned by `MainWindow`. It starts when the dashboard opens and stops when the dashboard hides. Therefore rich CPU/package-W/GHz history does not exist while the dashboard is closed.

The policy controller continues operating in the background, but the visual-history stream is UI-scoped.

### New boundary

Introduce an application-level `TelemetryContinuityRecorder` independent of any window.

The recorder owns one bounded telemetry history and exposes read-only snapshots to every visual surface.

Visual surfaces do not create independent telemetry sources. The dashboard and tray popup subscribe to the recorder and request a richer cadence while visible.

### Data sources

The recorder consumes two kinds of information:

1. **Controller snapshots and transitions** - already produced by policy operation. These provide CPU demand when policy sampling is active, current state, reason, threshold progress, latch state, and transition history without duplicating controller polling.
2. **Rich dashboard telemetry** - package watts and average frequency from the existing `DashboardTelemetrySource`, sampled adaptively.

The recorder merges these into timestamped continuity samples.

### Adaptive cadence

The cadence is explicit and testable:

| Context | Rich telemetry cadence | Purpose |
| --- | --- | --- |
| Dashboard or tray popup visible | 1 second | Smooth interactive inspection |
| Ordinary AUTO desktop, all visual surfaces hidden | 5 seconds | Low-cost continuity history |
| Game latch, all visual surfaces hidden | Off | Preserve game/background quietness |
| Manual latch, all visual surfaces hidden | Off | Respect intentional sampling suspension |
| Any latch with a visual surface explicitly opened | 1 second | User explicitly requested telemetry |

Controller/game lifecycle behavior is unchanged by this table.

A visual surface obtains a lightweight visibility lease. Multiple visible surfaces still produce only one 1 Hz telemetry source; closing the last lease returns the recorder to the appropriate hidden cadence.

### Bounded history

Initial in-memory capacity: enough for at least 60 minutes of 5-second continuity plus dense visible intervals. The implementation may choose a fixed count such as 1,500-2,000 samples rather than time-bucket complexity.

No continuous disk persistence is included in this phase. History resets on process restart. Durable long-term history can be designed later if real use demonstrates value.

### Intentional gaps

When rich hidden telemetry is suspended for a game/manual latch, the graph must show an honest gap or state-only interval. It must not interpolate fabricated watts/GHz across the gap.

State/latch episodes remain visible because they come from controller events.

### Degraded telemetry

If package watts or frequency are unavailable:

- CPU/state continuity remains useful;
- unavailable series are omitted or shown as a gap;
- the UI never substitutes zero;
- one telemetry-source failure does not stop policy operation.

## Recorder interfaces

The design should converge on small responsibilities rather than growing `MainWindow` or `App.xaml.cs` further.

### `TelemetryContinuityRecorder`

Responsibilities:

- own the bounded continuity ring;
- subscribe to controller snapshots/transitions;
- own the single adaptive rich-telemetry source;
- switch cadence based on current policy/latch state and visual visibility leases;
- expose immutable history snapshots and latest telemetry;
- publish a single continuity-changed event suitable for UI projection.

It does not make power-policy decisions.

### `TelemetryVisibilityLease`

Disposable token obtained by a visual surface while it is actually visible. It increments/decrements visible-demand state without creating another polling loop.

### `TrajectoryProjection`

Pure projection from continuity history + current controller snapshot + configuration into:

- state segments;
- transition marks;
- threshold rails;
- NOW position;
- policy-pressure / qualification progress;
- hover-lens data;
- expanded episode detail.

No Windows APIs and no timers.

### `DisclosureState`

Small UI state model describing what, if anything, is expanded:

- none;
- NOW;
- trajectory;
- quiet rail;
- promote rail;
- transition episode;
- mode node.

Only one primary expansion is active at a time in the first implementation. This prevents the dashboard from becoming a card wall again.

## Dashboard lifecycle

Opening the dashboard:

1. obtain a recorder visibility lease;
2. bind the current recorder history immediately, so the graph is populated on first frame;
3. rich cadence becomes 1 Hz;
4. UI animations run only while visible.

Hiding the dashboard:

1. release the visibility lease;
2. stop dashboard-only composition/animation;
3. recorder falls back to 5-second hidden AUTO cadence or zero rich cadence under a latch;
4. policy controller continues independently.

The tray popup follows the same lease model.

## Tray popup redesign

The current popup should be visually upgraded to become the Level-0 form of the same trajectory instrument.

It should contain:

- compact state/mode header;
- CPU / watts / GHz;
- a true continuity sparkline sourced from the recorder rather than a popup-local session;
- one NOW marker and recent state transition if present;
- one next-action sentence;
- subtle theme-aware depth/glow consistent with the main dashboard;
- no independent telemetry session.

Hover should feel polished, but the popup remains operationally cheap: no popup, no visibility lease, no 1 Hz rich telemetry.

## Responsive behavior

The default dashboard target remains approximately 960x620.

At narrower widths:

- typography never shrinks below the established floor;
- secondary labels collapse before primary values;
- trajectory remains the dominant visual;
- expanded detail can stack below the trajectory;
- Rules/Settings scroll vertically as necessary;
- no fixed-width dialog should force clipping.

## Motion

Normal mode:

- smooth path interpolation;
- subtle energy flow toward the relevant state;
- state transitions travel rather than flash;
- expansion uses short spatial morph/fade transitions;
- hover lens fades and slightly scales in place.

Reduced Motion:

- no travel animation;
- use crossfades and immediate geometry updates;
- information hierarchy and causality remain fully legible.

Hidden windows render nothing.

## Theme

All new trajectory, popup, lens, marker, rail, and expansion visuals use semantic ThemeResources. No design-critical hard-coded dark-only colors.

System, Light, and Dark remain selectable.

## Performance safeguards

1. Exactly one rich telemetry source per PowerFlow process.
2. No visual-surface-local telemetry polling after this redesign.
3. Hidden AUTO rich sampling no faster than every 5 seconds.
4. Hidden game/manual latch rich sampling is off.
5. History storage is bounded in memory.
6. Telemetry updates are coalesced so one recorder update produces one UI projection/update per visible surface.
7. No telemetry disk writes in this phase.
8. No animation/render timer exists while all visual surfaces are hidden.
9. Existing event-driven game detection remains unchanged.

## Failure behavior

- Recorder failure cannot stop or alter the policy controller.
- If the rich source throws, dispose it, mark rich telemetry degraded, and retry only at a bounded cadence when context next permits sampling.
- A failed sample creates a gap, not fake data.
- Visibility leases are idempotent/disposable; an abandoned window cannot permanently pin 1 Hz sampling.
- If a dashboard expansion refers to an event that ages out of the ring, close the expansion cleanly and return to the compact view.

## Testing strategy

### Recorder unit tests

- hidden AUTO selects 5-second rich cadence;
- visible dashboard selects 1-second cadence;
- visible tray popup selects 1-second cadence;
- two visible surfaces still create one telemetry source;
- closing the last visible surface returns to hidden cadence;
- hidden game latch turns rich telemetry off;
- hidden manual latch turns rich telemetry off;
- explicitly opening a visual surface during a latch enables 1 Hz telemetry;
- ring remains bounded;
- controller snapshots populate CPU/state continuity without duplicate activity polling;
- unavailable watts/GHz remain null/gaps;
- source exceptions do not affect the policy controller.

### Projection tests

- trajectory segments match historical state intervals;
- only real semantic transitions produce markers;
- NOW reflects current state and threshold progress;
- quiet/promote rail hover produces exact threshold/hold values;
- policy trajectory never predicts future CPU values;
- gap intervals remain gaps;
- one disclosure target at a time;
- expansion/collapse preserves selected episode identity.

### UI contract tests

- default dashboard no longer contains permanent Recent / Automatic Rules / Decision Pressure card-wall sections;
- trajectory is the dominant default surface;
- mode nodes and policy rails remain directly interactive;
- typography floor remains enforced;
- tray popup consumes recorder data rather than owning `DashboardTelemetrySession`;
- dashboard consumes recorder history immediately on open;
- Light/Dark/System semantic resources cover all new visuals.

### Integration/performance tests

- dashboard closed in AUTO accumulates continuity history;
- opening dashboard immediately shows pre-open history;
- opening/closing dashboard changes rich cadence 5 s <-> 1 s without duplicate samplers;
- hidden game latch creates no rich telemetry samples;
- process remains tray-only after dashboard close;
- no console/popups on normal startup;
- PowerFlow-on vs PowerFlow-off overhead gates from the original design remain mandatory.

## Acceptance criteria

The redesign is accepted only when all of the following are true:

1. Opening the dashboard after several minutes closed shows real pre-open history.
2. Default dashboard communicates past, NOW, policy pressure, and likely next policy action without opening another page.
3. Hover reveals exact detail locally.
4. Click expands detail in place and can collapse back to the same compact context.
5. The default dashboard is materially less card-like and more concise than the current build.
6. No readability regression is introduced to achieve compactness.
7. Tray popup feels like the compact form of the same instrument, not a separate design.
8. Recent/transition information changes only when meaningful events occur.
9. Hidden AUTO telemetry stays at 5-second rich cadence or slower.
10. Hidden game/manual latch has zero rich telemetry polling solely for visualization.
11. Full policy/game/manual semantics remain unchanged.
12. No implementation-time UI windows or cursor automation are launched on reference host without explicit user instruction.
13. Final human acceptance confirms the progressive-disclosure interaction feels coherent and polished.

## Scope exclusions

This phase does not include:

- persistent long-term telemetry across app restarts;
- cloud sync;
- ETW/GPU utilization monitoring for the dashboard;
- new game-detection heuristics;
- changes to the policy state machine or precedence;
- services/drivers;
- public GitHub publication.
