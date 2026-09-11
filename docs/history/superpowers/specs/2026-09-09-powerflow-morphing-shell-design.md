# PowerFlow Reference-Faithful Morphing Cockpit Design

**Date:** 2026-09-09
**Status:** Approved experiment; written-spec review pending
**Reference:** User-supplied PowerFlow dashboard image in the 2026-09-09 SysOps conversation.
**Supersedes:** The earlier 2026-09-09 morphing-shell design where the reference image was reduced to a graph-first dashboard with generic lower cards.

## 1. Goal

PowerFlow must feel like one polished operational cockpit that originates at the Windows tray and grows in place through **Glance → Compact → Expanded → Full Screen**.

The reference image is the information-architecture target, not merely a color palette. Expanded must visibly read like the supplied cockpit: strong left-to-right hierarchy, semantic power-mode controls, a dominant history/trajectory visualization, a real live-stats rail, explicit control cause/lock/rule context, secondary operational panels, and persistent system/status framing.

Compact and Glance are not smaller unrelated dashboards. They are progressively compressed presentations of the same cockpit and must preserve PowerFlow's five essential answers:

1. **What power state am I in?**
2. **What is the machine doing now?**
3. **How did it get here / where is it trending?**
4. **What is controlling the state right now?**
5. **What happens next?**

If a smaller state drops one of those five answers, the design has failed regardless of whether it fits.

## 2. Why the Current Experiment Failed

The first morphing-shell implementation got the window lifecycle mostly right but weakened the actual dashboard architecture.

The current `PowerFlowShellLayoutProfile` reduces the presentation to visibility booleans such as `ShowNavigationRail`, `ShowModeCards`, `ShowLiveStatsPanel`, and `ShowLowerContextPanels`. In Compact, live stats and lower context disappear. That is not progressive disclosure; it is information loss.

The current Expanded XAML also flattens the reference into:

- a three-item navigation rail;
- a brand card;
- four power-mode cards;
- one large trajectory panel;
- one generic stats/context panel;
- three equally weighted lower cards.

That misses the reference's stronger operational hierarchy: system identity/status, explicit lock/control state, a richer live-stat region, distinct control-context modules, meaningful secondary panels, and a more deliberate cockpit grid.

The corrected experiment keeps the successful **single-window / same-HWND** direction but replaces the deficient information architecture and shell layout model.

## 3. Product Principle: One Cockpit, Multiple Densities

PowerFlow has one conceptual dashboard and one set of primary controls. Window growth changes **presentation density, placement, and detail**, not product meaning.

Primary anchors must remain recognizable across all sizes:

- **Power state / mode control**
- **Live stats**
- **Trajectory/history**
- **Control context** (Auto, manual lock, game/app rule, trigger, cooldown)
- **Next action / policy direction**

Secondary modules may appear only when space allows, but their important meaning must already be represented by a primary anchor.

The implementation must not use a wall of unrelated cards to fill space. Relative visual weight communicates importance.

## 4. Canonical Shell States

### 4.1 Hidden

No visible PowerFlow window. The tray icon is the durable physical origin.

### 4.2 Glance — 320 × 176

**Purpose:** answer the five essentials in roughly one eye movement.

The shell grows from the real tray-icon rectangle and looks like a tightly cropped piece of the cockpit, not a separately designed popup.

Visible hierarchy:

1. compact brand/state line;
2. current mode plus Auto/lock status;
3. three truthful live values;
4. minimal shared trajectory/history visualization;
5. one compact cause/next line.

Example semantic structure:

`BALANCED · AUTO`
`CPU 12%   PKG 44 W   AVG 2.1 GHz`
`[trajectory]`
`Quiet for 4.2s → Saver`

The exact values vary with state and availability. No navigation, settings form, or lower-card wall appears here.

### 4.3 Compact — 760 × 440

**Purpose:** normal daily working dashboard.

Compact preserves all five essentials and introduces direct control without making the user open Expanded.

Composition:

- slim PowerFlow/device/status header;
- semantic four-mode selector in **segmented** density;
- trajectory/history as the main visual object, approximately two-thirds of the main row;
- condensed live-stats rail, approximately one-third of the main row;
- a single **control-context rail** underneath containing current control source, lock/rule state, policy progress/cooldown, and next action;
- compact affordances for Rules, Settings, and Expand that do not consume a permanent navigation rail.

The current Compact behavior that hides live stats and lower context is explicitly forbidden.

### 4.4 Expanded — canonical 1280 × 800

**Purpose:** closely reproduce the reference cockpit's information architecture using truthful PowerFlow data.

Expanded composition, top to bottom:

#### A. Left navigation rail

A persistent reference-style rail appears only at Expanded density and above. It contains real PowerFlow destinations only. Unsupported destinations are not shown merely to copy a screenshot.

Minimum real destinations:

- Dashboard
- Rules / Apps
- Settings

If a true Profiles concept is implemented later, it may occupy its own destination. Until then, no dead navigation item is permitted.

The rail also provides stable brand/system context so the main surface does not need an oversized standalone brand card.

#### B. System header

A slim horizontal header provides:

- PowerFlow identity;
- machine identity when available from a real provider;
- health/active status;
- preview/read-only status when relevant;
- Compact / Full Screen presentation actions.

It should resemble an application cockpit header, not a floating promotional banner.

#### C. Power-mode band

Four semantic controls occupy a strong horizontal band:

- Power Saver — green
- Balanced — blue/cyan
- Performance — orange
- Auto — purple

Each control has a stable identity and a presentation that scales by density. At Expanded it is a card with icon, name, concise description, and selected/lock semantics. The current state must be unmistakable without relying only on border color.

Manual/game latch state is not a fifth power plan. It is a **control-state overlay** on the current mode and also appears in Control Context.

#### D. Primary analytical row

The primary row uses the reference's asymmetric hierarchy:

- **left ~68–72%:** trajectory/history;
- **right ~28–32%:** Live Stats.

The graph remains the largest individual analytical object but is no longer the entire design thesis.

The Live Stats rail is a first-class region, not generic explanatory text. It uses real telemetry and compact gauges/numerical treatments appropriate to each signal.

#### E. Control-context band

A deliberate three-part operational band follows the primary row. Its conceptual roles mirror the supplied reference:

1. **Control / Manual Lock** — whether Auto, manual latch, or game latch owns the current state and how to release/change it.
2. **Active Rule / Cause** — triggering application/rule and the reason PowerFlow made or is holding the decision.
3. **Power Flow / Next** — threshold progress, cooldown/hold state, and likely next transition.

These are distinct questions and must not be flattened into three copies of the same generic card styling.

#### F. Secondary operational row

A lower row provides useful reference-style operational context where truthful data exists:

- **Recent Events** — recent state transitions with time and reason;
- **Active Sources / Consumers** — only if real data supports the label;
- **Quick Actions** — Rules, Settings, and context-appropriate actions.

Until per-process power is actually measured, the UI must not claim `Top Power Consumers` in watts. A truthful alternative such as **Active Sources** may show rule/trigger/app activity. If no useful source list exists, the region may use another real operational projection rather than fake data.

#### G. Status footer / framing

Expanded may use a subtle footer/status treatment for controller health, Auto/manual state, telemetry freshness, and/or app version when that data is available. It should be quiet visual framing, not another card.

### 4.5 Full Screen

Full Screen is an extension of Expanded, not another information architecture.

Extra space is used for:

- more graph height and temporal detail;
- more recent-event rows;
- richer live-stat descriptions;
- additional real source/rule rows;
- less truncation.

It must not introduce a different dashboard tree.

## 5. Morph Lineage: How Each Anchor Grows and Shrinks

The user must be able to visually track where information went during resize or state transitions.

### 5.1 Mode control lineage

- **Glance:** current-state capsule + Auto/lock badge.
- **Compact:** four-way segmented selector.
- **Expanded:** four semantic mode cards.
- **Full Screen:** same cards with slightly richer supporting detail.

This is one `PowerModeControl` with a density/presentation property, not independent mode-selector implementations.

### 5.2 Live Stats lineage

- **Glance:** three inline values.
- **Compact:** condensed vertical or 2×2 stats rail.
- **Expanded:** full right-side Live Stats module.
- **Full Screen:** same module with richer secondary labels/trends.

This is one `LiveStatsControl` bound to one stats projection.

### 5.3 Trajectory lineage

- **Glance:** no axes; state-colored minimal trajectory.
- **Compact:** compact axes/range, state bands, NOW, major threshold cues.
- **Expanded:** full readable history, legend/range controls, transitions, thresholds, hover detail.
- **Full Screen:** same graph with more plotting area and detail.

The same `TrajectoryControl` instance/concept remains the visual spine.

### 5.4 Control-context lineage

- **Glance:** one concise cause/next sentence.
- **Compact:** one horizontal control-context rail.
- **Expanded:** three distinct modules: Control/Lock, Active Rule/Cause, Power Flow/Next.
- **Full Screen:** same modules with additional explanatory detail.

### 5.5 Navigation lineage

- **Glance:** none.
- **Compact:** compact overflow/menu affordance; navigation consumes no permanent rail.
- **Expanded:** persistent left rail.
- **Full Screen:** same rail.

### 5.6 Header lineage

- **Glance:** tiny brand/state identity.
- **Compact:** one-row PowerFlow/device/status header.
- **Expanded:** full slim system header integrated with the left rail.
- **Full Screen:** same header.

## 6. Corrected Layout Architecture

The current boolean-heavy `PowerFlowShellLayoutProfile` must be replaced by a semantic density model.

Suggested conceptual model:

```text
ShellDensity
  Glance
  Compact
  Expanded
  Full

ShellPresentationProfile
  Density
  NavigationPresentation
  HeaderPresentation
  ModePresentation
  StatsPresentation
  TrajectoryPresentation
  ControlContextPresentation
  SecondaryPresentation
  Geometry / spacing / typography scalars
```

Example presentation enums:

```text
NavigationPresentation: None | Overlay | Rail
ModePresentation: CurrentChip | Segmented | Cards
StatsPresentation: Inline | CompactRail | FullRail
TrajectoryPresentation: Minimal | Compact | Full
ControlContextPresentation: CauseLine | Rail | Modules
SecondaryPresentation: Hidden | Summary | Full
```

The important distinction is that the layout engine decides **how a semantic region presents**, not merely whether it is visible.

### Component boundaries

`MainWindow` should become a shell/composition host rather than carrying the whole cockpit in one large XAML file.

Primary components:

- `ShellHeaderControl`
- `PowerModeControl`
- `LiveStatsControl`
- existing/refined `TrajectoryControl`
- `ControlContextControl`
- `OperationalContextControl`
- `ShellNavigationControl` or a small composition wrapper around native navigation

Each component receives a density/presentation profile and a view model. Primary components should not independently infer window size.

This keeps responsive behavior deterministic and allows each morph lineage to be tested in isolation.

## 7. Data Architecture and Truthfulness

### 7.1 Data PowerFlow already has

The existing dashboard can truthfully present:

- CPU utilization;
- package watts;
- average processor frequency;
- current power state;
- Auto/manual/game latch state;
- threshold progress;
- cooldown remaining;
- triggering application;
- decision reason;
- transition history;
- promotion and quiet thresholds/windows;
- rule projections.

### 7.2 Data PowerFlow does not currently have

The current source tree does **not** provide:

- GPU utilization/power/temperature;
- RAM utilization;
- CPU temperature;
- per-process power consumption;
- rich device identity/hardware inventory.

The experiment must not invent these values.

### 7.3 Telemetry-provider rule

The cockpit may add small, bounded providers where doing so materially improves the reference-faithful Live Stats area, but the UI architecture must not depend on every optional provider succeeding.

Recommended provider boundary:

```text
ILiveMetricProvider
  Name
  Availability
  SampleAsync()

LiveMetricSnapshot
  MetricId
  Value
  Unit
  Health/Freshness
  Optional trend
```

Provider priorities for this experiment:

1. preserve existing CPU/package/clock path;
2. add system memory usage if it can be read cheaply and reliably with native Windows APIs;
3. add basic machine identity cheaply and reliably;
4. treat GPU telemetry as optional/provider-specific work, not a blocker for the cockpit layout;
5. do not implement per-process `watts` unless a trustworthy measurement path exists.

Telemetry overhead remains part of PowerFlow's product contract: the monitoring UI must not become the load it is trying to manage.

## 8. Motion Architecture

Motion explains continuity; it is not decoration.

### 8.1 Window motion

The existing same-HWND direction remains correct:

- tray → Glance grows from the real tray-icon rectangle;
- Glance → Compact grows the same window;
- Compact → Expanded grows the same window;
- Expanded → Compact and Compact/Glance → tray reverse the spatial path;
- Full Screen extends Expanded rather than cross-fading to a different UI.

### 8.2 Interior motion

Primary anchors should translate, resize, or reform rather than blink out and be replaced.

Rules:

- avoid whole-dashboard crossfades;
- avoid simultaneous disappearance of multiple primary anchors;
- mode control begins changing form early in growth;
- graph continuously gains/loses detail as area changes;
- stats rail expands from the same metric cluster;
- control-context detail unfolds after sufficient space exists and collapses before shrink geometry becomes tight;
- navigation rail arrives late during expansion and leaves early during collapse;
- labels must remain readable during the transition, not merely at rest states.

Target durations remain fast and bounded, roughly **120–240 ms**, with distance-aware timing. Reduced Motion preserves the same information-state sequence but removes most interpolation.

## 9. Visual Language

The supplied image is the visual reference.

Required characteristics:

- deep navy/near-black canvas;
- subtle glass/Mica-like depth rather than flat gray cards;
- restrained cyan edge/highlight system;
- semantic green / blue-cyan / orange / purple power-state colors;
- selected state stronger through surface, glow, icon, and typography—not just a 1 px border;
- luminous but readable graph strokes;
- compact rounded surfaces with deliberate hierarchy;
- strong horizontal bands and asymmetric primary grid;
- minimal visual noise;
- no tiny text used to force content into a box.

Avoid:

- generic equal-weight card walls;
- giant empty hero regions;
- decorative telemetry that has no backing data;
- radial/circular treatment with no information benefit;
- gratuitous neon everywhere;
- a large standalone branding card that steals space from operational content.

## 10. Interaction Contract

### Tray

- hover may reveal transient Glance without activation;
- single click pins/activates Glance;
- clicking the pinned Glance surface grows it to Compact;
- double click may continue to open Compact directly if retained;
- right click remains the tray menu.

### Compact

- mode selection is directly usable;
- current controlling cause and next action are visible without expansion;
- Rules/Settings are one compact action away;
- Expand grows the same shell.

### Expanded

- persistent rail supports Dashboard / real management destinations;
- Rules and Settings remain within the same window lifecycle;
- Compact collapses back into the same shell;
- Full Screen extends the same composition.

### Close

Normal close returns toward the tray and leaves the background controller alive. Explicit Exit shuts down.

## 11. Responsive Rules

Canonical sizes are resting targets, not hard layout breakpoints.

- Glance rests near 320 × 176.
- Compact rests near 760 × 440.
- Expanded rests near 1280 × 800.
- manual resizing between Compact and Expanded continuously adjusts layout scalars and may graduate component presentations when sufficient space exists.

However, presentation changes must be coherent and atomic at semantic boundaries. For example, `PowerModeControl` should not half-render both Segmented and Cards at once.

No main dashboard scrollbar at canonical sizes. No explicitly sized visible text below 11 px.

## 12. Failure and Unavailable-Data Behavior

Optional telemetry is allowed to be unavailable. The UI should show a concise unavailable/stale treatment or substitute another truthful metric; it must not show zero as if it were a valid reading.

If an optional provider fails:

- controller operation continues;
- core CPU/package/clock telemetry continues if healthy;
- the affected metric is marked unavailable;
- no modal error or recurring popup appears;
- failure diagnostics remain bounded and non-spammy.

## 13. Testing Strategy

The previous implementation over-relied on source-contract tests that proved an element existed. The corrected experiment must test behavior and information roles.

### Pure layout tests

For each canonical and intermediate size, assert the semantic presentation profile:

- Glance answers all five essentials;
- Compact uses Segmented modes, CompactRail stats, Compact trajectory, and ControlContext Rail;
- Expanded uses Cards, FullRail stats, Full trajectory, ControlContext Modules, and navigation Rail;
- larger Expanded sizes change geometry scalars without changing conceptual architecture.

### Component tests

Each primary component gets density/presentation tests proving it can represent the same underlying state at Glance/Compact/Expanded densities without inventing data.

### Data tests

- unavailable optional metrics remain unavailable, never fabricated as zero;
- stale metrics are marked stale;
- existing package/clock semantics remain correct;
- rule/latch/cooldown projections remain consistent across densities.

### Motion/source contracts

- one visual shell/window owner;
- same-HWND `MoveAndResize` path remains;
- reverse shrink goes toward the tray anchor;
- Reduced Motion bypasses interpolation without changing final information state.

### Full regression

Core, Windows, and App tests must all pass on the exact candidate build before live acceptance.

## 14. Visual Acceptance Rubric

Automated tests do **not** constitute visual acceptance.

Before merge/publication, direct captures of the exact candidate must be reviewed at:

- Glance 320 × 176;
- Compact 760 × 440;
- Expanded approximately 1280 × 800;
- Full Screen.

Expanded acceptance questions:

1. At a glance, does it visibly share the reference image's cockpit hierarchy and relative visual weighting?
2. Is the power-mode band immediately legible and unmistakably interactive?
3. Is the graph the largest analytical object without swallowing the rest of the product?
4. Does Live Stats look like a real first-class instrumentation rail rather than a text card?
5. Are Control/Lock, Active Rule/Cause, and Power Flow/Next visibly distinct?
6. Does the secondary row feel operational rather than like filler cards?
7. Is machine/status framing present without wasting space?
8. Is there any fake or unsupported telemetry?

Cross-density acceptance questions:

1. Can a reviewer visually track state, stats, graph, cause, and next action while growing/shrinking?
2. Does Compact still answer all five essential questions?
3. Does Glance still answer all five essentials without looking cramped?
4. Are there no clipping/overlap/tiny-text failures during animated transitions?
5. Does Reduced Motion remain coherent?

The experiment is not accepted until the user reviews the actual UI, not merely screenshots or tests, and says the information architecture is right.

## 15. Non-Goals for This Experiment

- packaging/signing/installer work;
- unrelated controller-policy changes;
- BIOS or hardware power tuning;
- fake GPU/RAM/temp values to mimic the reference;
- exhaustive hardware-monitoring support;
- unrelated Rules/Settings redesign except what is required for same-shell continuity;
- changing the established power-plan policy semantics.

## 16. Implementation Sequencing Constraint

Once this written spec is approved, the implementation plan must follow this order:

1. semantic layout/density model and RED tests;
2. componentize the five primary anchors without changing behavior;
3. rebuild Expanded to match the reference hierarchy first;
4. derive Compact from those same components;
5. derive Glance from those same components;
6. add only bounded truthful telemetry/provider enhancements needed for Live Stats;
7. choreograph cross-density motion and reverse shrink;
8. full automated regression;
9. live same-HWND acceptance at all four densities;
10. user visual review;
11. README screenshots/documentation only after visual acceptance;
12. cleanup, merge, publication only after approval.

Expanded-first is deliberate: the reference cockpit is the source architecture. Compact and Glance must be compression products of the correct Expanded design, not independent layouts invented in parallel.

## 17. Success Definition

This experiment succeeds when PowerFlow no longer feels like a graph with cards around it. It should feel like the supplied cockpit condensed into one coherent physical object:

- **Expanded** closely matches the reference's hierarchy and visual rhythm;
- **Compact** retains the same operational essentials in a denser arrangement;
- **Glance** retains those essentials in one glance;
- transitions make it obvious that each state grew from the one before it;
- every displayed value is real or clearly unavailable;
- the user can understand current state, live condition, recent trajectory, controlling cause, and next action without hunting.
