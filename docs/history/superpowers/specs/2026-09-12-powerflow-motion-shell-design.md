# PowerFlow Motion Shell Design

**Date:** 2026-09-12
**Status:** Approved

## Purpose

PowerFlow must behave like one physical, continuously evolving object rather than a set of dashboard windows and breakpoints. The experience begins at the system tray icon, grows into a lightweight live view, expands into a moveable live dashboard, then reveals progressively richer controls and context as the user explicitly asks for more space or manually resizes it. Page navigation never owns window geometry.

The shell must first be mechanically correct: moveable, continuously repainting while resized, stable under navigation, and deterministic under DPI changes. Motion polish is built only on that substrate.

## Product States

PowerFlow exposes five primary presentation levels plus an optional fullscreen mode:

1. **Tray** — persistent notification-area presence. Static when settled; short, efficient motion conveys policy/governor transitions.
2. **Peek** — tray-anchored, no-activate hover surface. View-first telemetry and current governor state, with at most one posture/tension control.
3. **Live** — pinned compact dashboard. Moveable, resizeable, and still visually continuous with Peek.
4. **Dashboard** — medium working view with richer timeline, governor comparison, navigation, and contextual controls.
5. **Workspace** — full application working size, approximately 1360×860 logical pixels by default. All major options and detail panels are visible without relying on drawers.
6. **Fullscreen** — optional presentation mode, not the normal largest application state.

Current internal states may be preserved where useful for compatibility, but the public experience and transition semantics must map to Tray / Peek / Live / Dashboard / Workspace.

## Geometry Authority

Window geometry has exactly one owner: `ShellGeometryAuthority`.

- Navigation changes content only.
- Selecting Workloads, Baseline, Settings, or Live must never resize or move the window.
- Only explicit user presentation commands, tray-origin opening, fullscreen entry/exit, or direct user move/resize change native bounds.
- Once the user moves Live/Dashboard/Workspace, subsequent growth/shrink transitions preserve the visible anchor and expand/contract around the current window rather than snapping back to the tray.
- Peek remains tray-anchored because it is transient.
- Shrinking from Live to Peek may return to the tray anchor only when the user explicitly chooses the tray/peek collapse path.

## Frame Mechanics

### Dragging

The visible header is the custom title bar. It must be registered with Windows as the drag surface in pinned states. Interactive regions — navigation, buttons, sliders, charts with pointer interactions, and window controls — are excluded from drag rectangles.

Peek is transient and not manually moveable. Live, Dashboard, Workspace, and optional Fullscreen exit state are moveable where Windows permits it.

### Manual Resize and Repaint

Native `AppWindow.Changed` size events are input samples, not commands to rebuild the entire semantic shell immediately.

`ShellRenderScheduler` coalesces resize events and applies at most one lightweight presentation update per composition/render frame. During active resize it may update:

- composition scale/offset/clip,
- interpolated padding/gaps,
- chart viewport dimensions,
- continuous opacity/detail progress,
- non-allocating geometry values.

It must not repeatedly toggle large XAML subtrees, reselect navigation, rebuild policy surfaces, or run expensive semantic resets for every native size event.

After resize settles for 90–140 ms, a single semantic commit resolves the endpoint presentation and performs any required visibility/layout changes. The native frame and visual surface must stay visually attached throughout the drag.

## Motion Architecture

All automatic shell transitions use one `ShellMotionCoordinator`. There is one normalized transition timeline per shell transition, and all participating visual systems consume that timeline or a physically derived child timeline. No subsystem may run an unrelated animation that causes headers, graphs, controls, and frame bounds to arrive at visibly different times.

### Physical Material Families

Motion is modeled as material behavior, not generic easing.

**Rigid / inertial**
- Use for window translation, navigation rail motion, title/header blocks, and controls that should feel mechanically attached.
- Preserve velocity direction.
- No visible overshoot for native window bounds.
- Ease-in is brief; deceleration dominates the landing.

**Compliant / spring**
- Use for cards, control groups, badges, and secondary panels emerging inside an already-moving shell.
- Bounded overshoot: maximum 2.5% of travel or 6 logical px, whichever is smaller.
- One overshoot lobe maximum; no repeated bouncing.
- Settle to within 0.5 px / 0.5% opacity by transition completion.

**Fluid / surface growth**
- Use for Tray→Peek and Peek→Live shell reveal, and for chart/panel clipping masks that appear to grow from the originating surface.
- Growth begins at the real tray icon rectangle when tray geometry is available.
- Surface may show slight squash/stretch perpendicular to primary motion, bounded to ±3% scale.
- Area expansion must remain positive and continuous; no inversion, teleport, or midpoint snap.

### Momentum and Interruptions

If a transition is interrupted by a new target, the next motion begins from the current on-screen geometry and current estimated velocity. It must not restart from the previous semantic endpoint. Reversing direction dissipates velocity through the selected material damping rather than instantaneously flipping the object.

### Reduced Motion

Windows animation preference and the existing PowerFlow reduced-motion override remain authoritative. Reduced-motion mode keeps the same state/geometry model but uses near-immediate, non-overshooting transitions and still preserves correct origin/endpoint placement.

## Tray Motion

The tray icon should efficiently communicate governor activity using the PowerFlow swoop visual language.

- Idle/steady state uses a static icon; no continuous timer.
- Governor/state changes may trigger a short cached frame sequence (target 350–700 ms, 8–12 fps maximum).
- Efficiency movement should visually settle inward/downward; response/performance movement should sweep outward/upward using the existing swoop motif.
- Manual lock/override should have a distinct held/static state rather than constant animation.
- Icon frames are cached; HICON resources are reused and disposed deterministically.
- Tray animation must stop completely when settled.

The tray icon rectangle returned by `Shell_NotifyIconGetRect` is the canonical origin for Peek and the first pinned growth transition.

## Progressive Disclosure

Presentation depth derives continuously from available content space, not from page identity.

### Peek
- Current status / governor zone.
- Concise telemetry trajectory.
- Current AUTO vs Tension Shadow summary when available.
- At most one Tension/posture control.
- No navigation rail.

### Live
- Primary timeline and current telemetry.
- Current AUTO vs Tension Shadow identity and current recommendation.
- Compact tension/posture control.
- Minimal navigation affordance.

### Dashboard
- Richer timeline labels and history.
- Governor envelope comparison and workload actor context.
- Full navigation rail.
- Baseline-aware evidence where measured.

### Workspace
- All Dashboard content plus full policy/workload/baseline/settings controls visible in-page where practical.
- No requirement to open hidden drawers for primary configuration.
- Optional fullscreen is a presentation expansion of Workspace, not a different product mode.

Detail appearance uses progressive opacity/clip/position changes driven by shell progress; abrupt `Visibility` changes occur only after an element is fully visually absent or at final semantic commit.

## Tension Shadow Integration

The existing Tension Shadow model remains observational in this phase.

- Current AUTO remains the only automatic actuator.
- Tension Shadow consumes the same telemetry and continuously evaluates what it would do.
- Shadow output must never call `ApplyAdaptiveGovernorEvaluationAsync`, `PowerModeProfileRuntime.Apply`, Windows power-plan APIs, or controller actuation.
- The UI labels current behavior as `APPLIED` and shadow behavior as `SHADOW` / `WOULD USE`.
- Shadow envelope visualization is visually subordinate and non-interactive.
- Measured baseline evidence may be shown only when a matching fixed profile measurement exists; otherwise copy remains qualitative.

## Navigation Semantics

Navigation is content selection, not shell-size authority.

- Live, Workloads, Baseline, and Settings are selectable at any pinned window size.
- If a section needs more content space than available, it reflows or scrolls inside the current frame.
- The UI may suggest `Expand` / `Workspace` when additional space would improve usability, but it must not change native window bounds automatically.

## Installation and Launch

PowerFlow is currently an unpackaged WinUI application (`WindowsPackageType=None`). The deliverable must provide a repeatable local installation flow that publishes the self-contained app to a stable per-user install location and creates launch affordances.

Required install behavior:
- Install to `%LocalAppData%\Programs\PowerFlow` unless an explicit machine-wide installer is adopted later.
- Create a Start Menu shortcut named `PowerFlow`.
- Create a Desktop shortcut named `PowerFlow` unless installation is invoked with a no-desktop-shortcut option.
- Shortcut launches the normal tray runtime and opens the Live dashboard on explicit user launch.
- Start-with-Windows remains controlled by PowerFlow's existing setting rather than being forced by installation.
- Upgrade replaces app payload without deleting `%LocalAppData%\PowerFlow` configuration/history.
- Uninstall removes installed binaries/shortcuts but preserves user data unless the user explicitly requests data removal.

A signed/MSIX distribution can replace this local installer later, but this phase must leave the user with a stable installed path and shortcuts rather than a build-tree executable.

## Acceptance Criteria

### Frame correctness
- Pinned window can be dragged from the visible header at Live, Dashboard, and Workspace sizes.
- Interactive controls in the header remain clickable and do not initiate drag.
- Manual resize continuously repaints with no visibly stale regions.
- Resize across density boundaries has no one-frame semantic snap or blank frame.
- Navigation never changes native position or dimensions.
- User move position survives page changes and subsequent manual resize.

### Motion quality
- Tray→Peek begins at the measured tray icon rectangle when available.
- Peek→Live, Live→Dashboard, Dashboard→Workspace and all reverse transitions are continuous.
- Native bounds never reverse direction during a monotonic grow/shrink transition unless the user reverses the command.
- Compliant internal elements remain within the overshoot bounds above.
- Interrupted transitions continue from current geometry and estimated velocity.
- Reduced-motion mode has correct endpoints and no overshoot.

### Performance
- Manual resize semantic presentation updates are coalesced to at most one per render-frame callback/tick.
- No background tray animation timer runs while the icon is settled.
- The timeline/telemetry sampler remains independent of whether the dashboard is visible.

### Product behavior
- Hover Peek is no-activate and view-first.
- Live/Dashboard/Workspace are moveable and resizeable.
- Workspace exposes the major controls without requiring fullscreen.
- Tension Shadow remains observational.

### Installation
- Fresh install launches from Start Menu shortcut.
- Desktop shortcut launches the same installed executable.
- Upgrade preserves configuration/history.
- Uninstall removes installed payload and shortcuts.

## Explicit Non-Goals

- Tension Shadow does not become the actuator in this phase.
- Do not redesign telemetry collection or baseline algorithms unless required to keep shell rendering responsive.
- Do not add new navigation sections.
- Do not require fullscreen for normal operation.
- Do not add decorative perpetual animation.