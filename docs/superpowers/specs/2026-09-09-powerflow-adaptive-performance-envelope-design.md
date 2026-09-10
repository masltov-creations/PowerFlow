# PowerFlow Adaptive Performance Envelope Design

## 1. Product Thesis

PowerFlow is a visual performance governor for Windows PCs. Applications generate demand; PowerFlow decides how much machine response that demand is entitled to receive. The product keeps a machine near its efficient operating region by default, then grants additional performance when measured evidence says the extra power materially benefits the user.

PowerFlow is not fundamentally a power-plan switcher. Windows power plans, processor energy-performance preference, core parking, promotion/decrease behavior, boost policy, and future qualified process-level controls are actuators beneath a higher-level behavioral model.

The primary initial use case is a homogeneous-core desktop where background or moderately demanding applications can keep expensive CPU resources awake or encourage unnecessarily aggressive performance behavior. The system must avoid crude hard limits that simply make useful work take longer. Its goal is minimum energy, heat, and noise for the required user experience, not minimum instantaneous watts.

## 2. Core Behavioral Model

The machine exposes an **Adaptive Performance Envelope** with understandable operating zones:

- **Eco** — very low demand; prioritize sleep, consolidation, parking, and low-power residency.
- **Efficient** — the learned sweet region for ordinary work; this is the preferred default operating envelope.
- **Responsive** — additional resources are justified for foreground or latency-sensitive work.
- **Boost** — expensive performance is released temporarily when evidence predicts worthwhile benefit.

These zones are not fixed mappings to Windows power plans. They are behavioral regions backed by machine-specific actuators. The exact actuator recipe may differ by CPU generation and platform.

PowerFlow maintains four conceptual layers:

**Sensors -> Pressure -> Policy -> Actuators**

Sensors measure what the machine and workloads are doing. Pressure converts raw telemetry into a workload-demand interpretation. Policy determines whether that demand is entitled to move farther into the envelope. Actuators apply the qualified response.

## 3. Performance Entitlement

Every observed application or workload may inherit or override a **Performance Entitlement**. Entitlement defines how far that actor is normally allowed to push the machine and under what conditions temporary escalation is permitted.

Examples:

- Browser background activity: ceiling at Efficient unless foreground evidence justifies more.
- Compiler/build: Efficient by default, Responsive/Boost lease when sustained throughput gains are observed.
- Game: latency-sensitive entitlement that can reach Boost while foreground/fullscreen conditions hold.
- Updater/indexer/RGB/launcher: strongly efficiency-biased background entitlement.

Entitlements are policy, not hard CPU quotas. They control machine response first. Future process-specific mechanisms may be added only when they are demonstrably safe and beneficial.

## 4. Boost Leases

Escalation above the ordinary efficient envelope is granted as a temporary **Boost Lease**, not an indefinite state switch.

A lease has:

- qualifying pressure threshold;
- qualifying duration/dwell;
- actor/workload entitlement;
- predicted or learned benefit confidence;
- maximum envelope region allowed;
- lease duration;
- renewal conditions;
- release hysteresis / quiet period;
- reason and evidence.

A workload can renew a lease while useful pressure persists. When justification disappears, PowerFlow closes the lease smoothly rather than oscillating rapidly between states.

Every grant, denial, renewal, and release must be explainable in plain language and traceable to observed data.

## 5. Machine-Specific Calibration and Learning

PowerFlow learns an **efficiency map** for each machine rather than assuming Windows plan names describe efficient behavior.

Learning combines:

1. **Passive observation** of real workloads over time.
2. **Optional bounded calibration** runs initiated or scheduled by the user, designed to establish controlled reference points without unexpectedly monopolizing the machine.

The model must distinguish at least these workload shapes:

- short interactive / race-to-idle bursts;
- sustained lightly parallel work;
- sustained moderately parallel work;
- heavily parallel throughput work;
- background nuisance activity;
- latency-sensitive foreground workloads.

A single universal efficiency knee is not assumed. PowerFlow learns workload-conditioned efficiency frontiers and carries confidence for each region/model.

When BIOS, CPU policy, cooling, hardware, or other material system configuration changes, the model must mark affected learned regions stale rather than silently treating old calibration as current truth.

## 6. User Control: Learned, Tuned, Override

Automatic behavior never removes user agency. Every meaningful policy control has three conceptual layers:

- **Learned** — PowerFlow's current machine-specific recommendation.
- **Tuned** — user adjustment relative to the learned value, preferably expressed as behavioral intent rather than obscure Windows internals.
- **Override** — explicit app/workload or machine policy that supersedes automatic learning.

The default UI exposes semantic controls such as Efficiency Bias, Boost Qualification, Release Quiet, Core Expansion, and App Entitlement. An Expert / Actuators disclosure shows the exact underlying Windows settings and mappings PowerFlow chose.

User overrides are durable, visible, reversible, and never silently rewritten by learning.

## 7. Central Analytical Instrument

PowerFlow has one shared analytical canvas with multiple projections. Selection, time range, app/workload filter, policy boundary, and inspection state persist as the user changes projections.

### 7.1 Timeline Projection

Timeline is the default Observe view. It is one synchronized time canvas with aligned KPI lanes, not a collection of unrelated dashboard cards.

Initial required lanes:

- CPU demand / pressure;
- CPU package power;
- effective / average clock;
- active or unparked core count when truthfully measurable;
- current Adaptive Performance Envelope region / actuator state;
- application attribution and decision events.

All numeric lanes share one X/time axis and one inspection cursor. Values with incompatible units use aligned lanes rather than dishonest multiple Y-axes in one plot.

Optional **Normalized Overlay** maps selected KPIs onto normalized 0-100 behavioral ranges to reveal correlation and divergence. The UI must clearly label normalization so it cannot be mistaken for raw units.

### 7.2 Timeline Policy Rails

Policy thresholds are drawn on the same time canvas they govern. Examples:

- pressure required to qualify for Responsive;
- qualification duration;
- release / quiet threshold and hysteresis;
- learned power efficiency frontier;
- core-expansion policy where supported;
- per-application entitlement ceiling.

These rails are inspectable and, in Tune mode, adjustable. Adjustments must preserve direct access to the learned position and user offset.

### 7.3 Counterfactual Preview

Before a tuning change is committed, PowerFlow replays the proposed policy against retained recent observations when sufficient evidence exists. It reports only metrics supportable by observed data and labels uncertainty.

Examples:

- number of boost leases that would have changed;
- time redistributed among envelope regions;
- estimated energy/power difference;
- estimated observed-workload performance impact;
- confidence / insufficient-evidence status.

Counterfactuals are never presented as certainty when the data cannot support causal inference.

## 8. Performance Atlas Projection

The Atlas is the Understand projection of the same observations. It exposes the machine's operating shape and learned efficiency frontier.

The underlying dataset is multidimensional. The UI projects it into readable 2D views instead of encoding every variable simultaneously.

Selectable dimensions may include:

- package power;
- effective clock;
- active cores;
- CPU pressure;
- observed workload throughput/benefit where measurable;
- envelope region;
- residency/time;
- application/workload class.

Default visual grammar:

- X = one selected dimension;
- Y = second selected dimension;
- density/intensity = residency or observation count;
- one additional encoding = efficiency or another explicitly selected metric;
- contours/overlays = learned frontiers or envelope regions;
- event markers = lease/brake/override decisions;
- application/time = filter/highlight dimensions rather than an uncontrolled palette explosion.

An Expert 3D exploratory projection may later expose three continuous axes with density/efficiency encoding, but 2D remains the primary analytical view because it supports more accurate comparison.

## 9. Linked Timeline <-> Atlas Interaction

Timeline and Atlas are two projections of the same selected observations.

- Selecting an expensive cluster in Atlas highlights every corresponding time interval in Timeline.
- Brushing a suspicious interval in Timeline highlights where that episode sits in Atlas.
- Selecting an application persists across both projections.
- Selecting a machine boundary or boost lease persists across both projections.
- Tuning a boundary may be previewed against both temporal history and operating-density redistribution.

The product should feel as though the same data is reorganizing into a different projection, not as though the user navigated to an unrelated analytics page. Reduced Motion switches projections without geometric animation.

## 10. Progressive Workflow

The central experience follows three progressive modes over the same instrument:

- **Observe** — live Timeline, current actor, current envelope, current decision.
- **Understand** — Atlas, explanations, attribution, learned frontiers, anomaly/inefficiency insights.
- **Tune** — policy rails, learned positions, user offsets, entitlement gates, counterfactual preview, commit/revert.

These are modes of one analytical workspace rather than separate Dashboard / Analytics / Rules pages.

## 11. Explainability Contract

At any important moment PowerFlow should be able to answer:

1. What is the machine doing now?
2. What workload/app is creating pressure?
3. Where is the machine in its learned envelope?
4. What boundary is being approached or crossed?
5. What did PowerFlow allow, deny, or delay?
6. Why?
7. What happens next if conditions remain the same?
8. What would the relevant user lever change?

Example explanation:

> Chrome pressure crossed the Responsive threshold. Chrome entitlement is Efficient. Escalation was denied because observed benefit in this workload class is below the learned frontier. If pressure remains below the release threshold for 9 more seconds, PowerFlow will return to Eco.

No explanation may assert attribution, power, performance gain, or actuator state that was not actually observed or deterministically derived.

## 12. Telemetry and Truthfulness

Initial high-value KPIs are intentionally narrow:

- CPU pressure/utilization;
- package power;
- effective/average clock;
- active/unparked cores if a reliable low-overhead provider is qualified;
- current envelope and actuator state;
- application/process attribution sufficient to explain pressure;
- transitions, latches, leases, and policy decisions.

Memory, disk, network, GPU, thermal, fan, and other telemetry are not added to the primary analytical instrument merely because they are measurable. A KPI joins the default instrument only if it materially explains performance spent versus benefit received and can be collected truthfully with acceptable overhead.

Unavailable data renders explicitly unavailable; it is never synthesized.

## 13. Learning Safety and Control-System Guardrails

PowerFlow is a governor, so adaptation must be bounded and reversible.

- Learned changes never silently overwrite explicit user tuning or overrides.
- Learning rate and policy changes are bounded; the system cannot swing aggressively from one observation.
- Hysteresis prevents oscillation.
- Calibration cannot unexpectedly launch a heavy benchmark or monopolize resources.
- Model confidence is visible and materially affects automatic decisions.
- Every actuator has a safe fallback behavior.
- A failure in learning/attribution must degrade toward conservative, understandable policy rather than uncontrolled escalation.
- The user can pause learning while retaining current policy.
- Raw observations and policy decisions are separable so a policy/model revision can be replayed against retained history.

## 14. Visual Language

The instrument is dense, compact, and cockpit-like, but not decorative for its own sake.

- Deep dark surfaces with restrained semantic color.
- Lines and rails carry meaning; glow is reserved for active pressure, selected data, or a live lease.
- Boundaries may show subtle elastic pressure as demand qualifies against them, but motion remains instrument-like rather than game-like.
- The graph is the primary analytical object, not a giant empty hero.
- Compact and Glance are lower-density views of the same semantic model.
- No text below 11 px.
- No fake telemetry or unlabeled normalized values.

## 15. Canonical Visual Evolution: Tray -> Glance -> Compact -> Expanded -> Full Screen

The presentation contract is one physical instrument unfolding from the notification area. These are not separate dashboards and they must not feel like navigation between pages. The same selected actor, telemetry window, policy decision, threshold, and machine state maintain visual lineage as density increases.

### 15.1 Tray

The tray icon is the smallest expression of the governor. Its state treatment communicates whether PowerFlow is calmly governing, actively braking an escalation, granting a temporary lease, paused, or unhealthy. It does not attempt to show numeric telemetry.

### 15.2 Glance - approximately 320 x 176

Glance answers the immediate five questions without requiring interpretation:

- what envelope the machine occupies now;
- current package power, effective clock, and active-core summary;
- a tiny synchronized trend derived from the same Timeline data;
- the notable application/workload actor and whether PowerFlow is braking or leasing performance;
- what PowerFlow expects to do next.

Canonical information form:

```text
EFFICIENT                         BRAKING
42 W · 2.0 GHz · 4/16 cores
CPU/PWR  ▁▂▃▅▆▅▃▂▂▃▅
Chrome -> held at Efficient
Next: continue Efficient · qualify 2.1 / 4.0 s
```

Glance is observational. It may expose one obvious grow affordance but does not become a miniature settings form.

### 15.3 Compact - approximately 760 x 440

Compact reveals the first real analytical instrument. The tiny Glance trend expands into a short synchronized multi-KPI Timeline with a single shared time axis. The default lanes are CPU pressure, package power, effective clock, active/unparked cores, and the envelope/decision lane. App attribution and brake/lease markers remain attached to the same observations.

Compact also exposes one high-value semantic tuning control, typically the overall Efficiency <-> Responsiveness bias, plus the currently active decision and next transition. It preserves all five Glance answers while adding enough history to understand whether the current state is transient or sustained.

### 15.4 Expanded - canonical approximately 1280 x 800

Expanded is the primary PowerFlow cockpit. The synchronized Timeline occupies roughly the upper two-thirds and is the dominant analytical object.

A narrow persistent rail provides Live, Apps, Model, and Tune as progressive analytical modes rather than generic Dashboard/Rules/Settings destinations. Settings remains secondary.

The Timeline requirements are:

- one shared time axis and inspection cursor for all KPI lanes;
- aligned lanes for CPU pressure, package power, effective clock, active/unparked cores, and envelope/actuator state;
- application/workload attribution attached to intervals and events;
- learned Eco / Efficient / Responsive / Boost boundaries drawn as meaningful rails;
- visible qualification progress, granted boost leases, denied escalation, release hysteresis, manual overrides, and actuator changes;
- draggable semantic thresholds where safe, with expert actuator values disclosed beneath rather than used as the primary language.

The lower third is a contextual control surface, not a permanent card wall. With nothing selected it shows the learned machine envelope and the few highest-value machine levers. Selecting an app, lease, threshold, or interval morphs the right-side context into that object's entitlement, explanation, tuning, and evidence without replacing the Timeline.

### 15.5 Full Screen

Full Screen earns its extra area by revealing a second projection of the same observations rather than stretching Expanded.

The upper region remains the synchronized Timeline. The lower region exposes the multidimensional Performance Atlas alongside contextual explanation/tuning. Timeline and Atlas remain selection-linked: brushing an inefficient Atlas cluster highlights the exact corresponding time intervals, and selecting a Timeline burst highlights where that episode sits relative to the learned efficiency frontier.

The Atlas default is a two-dimensional density projection such as effective clock x package power, with residency/density plus one additional meaningful encoding such as learned efficiency. Other dimensions can be projected or filtered without turning the default view into an unreadable multi-axis graphic.

Full Screen may provide deeper comparison and counterfactual controls, but it remains the same cockpit architecture and same selected analytical context.

### 15.6 Morph Lineage

Expansion must preserve object identity wherever possible:

- the Glance sparkline physically becomes the synchronized Timeline rather than disappearing and being replaced;
- `42 W · 2.0 GHz · 4/16` separates into the Package Power, Effective Clock, and Active Cores lanes;
- `Chrome - brake active` grows into the same actor marker and entitlement gate on the Timeline;
- the compact Efficiency <-> Responsiveness control unfolds into the machine envelope rails and semantic threshold levers;
- in Full Screen, selected Timeline observations reorganize into the Performance Atlas while retaining selection and context.

The desired perception is that the user starts with a tiny live instrument near the tray and progressively unfolds it until they are effectively standing inside the machine's control system.

### 15.7 Visual Priority

Named Windows power plans are implementation actuators, not the dominant visual abstraction. Saver, Balanced, High Performance, and similar plan names may appear in Expert/Actuator disclosure, but the primary surfaces prioritize machine state, KPI trajectories, learned performance envelope, application pressure, policy decisions, and user-adjustable semantic levers.

## 16. Current Windows Actuator Evidence on reference-host

Read-only inspection on the 16-core / 32-thread AMD Ryzen 9 5950X shows why PowerFlow must model behavior rather than plan names.

On AC power, the currently observed Balanced and Power Saver policies differ materially in processor behavior. Balanced uses a 5% minimum processor state, 100% maximum, EPP 10, processor performance increase threshold 30%, decrease threshold 10%, and minimum unparked cores 100%. Power Saver also allows a 100% maximum and Aggressive boost, but uses EPP 60, performance increase threshold 90%, decrease threshold 60%, slower increase timing, and minimum unparked cores 10%.

Therefore Power Saver on this machine is not simply a hard percentage cap. It changes how eagerly Windows spends performance: it favors efficiency, permits aggressive parking, and requires much stronger sustained pressure before increasing performance while still retaining headroom when justified.

These observed values are evidence for the design, not universal defaults. PowerFlow must discover the available actuator behavior per machine.

## 17. reference-host UI Safety — HARD OPERATING RULE

reference-host is an actively used desktop and gaming machine. Development and validation MUST NOT create visible desktop interference without fresh explicit user authorization for the specific live-UI action.

Without that authorization, executors MUST NOT launch visible PowerFlow windows, use preview/popup-preview/dashboard/fullscreen launch modes, interact with the tray, invoke UI Automation or synthetic input, focus/activate/move/resize/capture a PowerFlow window, or start helpers that can display consoles/dialogs/toasts/prompts.

Default execution is headless only: source/document edits, tests, builds, static inspection, and background-safe telemetry/log inspection proven not to surface UI.

General phrases such as "continue", "proceed", "finish", or a prior approval for a different UI action do not authorize live UI. If an acceptance criterion requires visible UI and no explicit authorization exists, work stops at that gate and reports the pending visual validation.

## 18. Scope and Sequencing

This design is the product architecture that follows the current reference-cockpit shell work. It does not require discarding the completed morphing-shell foundations.

Implementation should proceed in independently useful slices:

1. observation schema and retained synchronized KPI series;
2. headless learned-envelope/calibration model with no behavior changes;
3. governor policy model and explainable simulated decisions;
4. Timeline projection backed by real observations;
5. policy rails and read-only learned boundaries;
6. Performance Atlas and linked selection;
7. counterfactual replay;
8. guarded user tuning / override persistence;
9. actuator integration behind safety gates;
10. app entitlements and boost leases;
11. only after all prior headless/automated qualification, explicitly authorized live visual acceptance.

No actuator mutation should be introduced merely to make a visualization demo work. Observation, modeling, simulation, and user-facing explanation are qualified before automatic control authority expands.

## 19. Success Definition

PowerFlow succeeds when the user can see and understand how the machine spends performance, identify the applications/workloads responsible for expensive operating regions, see the learned efficient frontier, understand why a boost was granted or denied, tune or override those decisions visually, and allow the machine to remain efficient by default while still releasing real performance when evidence says it is worth the cost.
