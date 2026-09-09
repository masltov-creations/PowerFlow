# PowerFlow Morphing Shell Design

**Date:** 2026-09-09  
**Status:** Proposed for implementation  
**Reference:** User-supplied PowerFlow dashboard image in the 2026-09-09 SysOps conversation.

## Goal

Make PowerFlow feel like one polished, physical instrument that originates at the Windows tray and grows in place from a tiny glance into a compact dashboard and then an expanded cockpit. The expanded visual language should closely follow the supplied reference: deep navy glass, luminous cyan/teal accents, restrained purple/orange state color, strong hierarchy, rounded panels, clean data visualization, and a premium native-Windows feel.

This is an implementation change to the real WinUI app, not a generated mockup.

## Product Principle

There is one PowerFlow object, not a popup plus a dashboard.

The user should be able to understand every size change spatially: the same state, telemetry, trajectory, and controls either move, unfold, condense, or gain detail. A view must never appear to be replaced by an unrelated view merely because the window crossed a breakpoint.

## Shell States

### Hidden

No visible PowerFlow window. The tray icon remains the durable origin.

### Glance

Canonical size: **320 x 176**.

Purpose: instant state and trend inspection. The shell is anchored immediately above the real tray icon. Hover may reveal it without activation. A single tray click pins/activates this exact same shell.

Visible content:

- PowerFlow mark/name;
- current power state and concise reason;
- three compact telemetry values using only real available telemetry;
- the shared trajectory graph in minimal density;
- one clear affordance to open/grow PowerFlow.

No navigation rail, rules panel, settings panel, or card wall.

### Compact

Canonical size: **760 x 440**.

Purpose: the normal working dashboard.

The Glance shell grows outward from the tray-origin geometry into Compact. The shared trajectory remains visually continuous and becomes the dominant panel. State selection becomes more explicit. Current rule/context and quick actions appear only when space permits.

Visible content:

- compact branded header;
- adaptive power-mode selector;
- trajectory/history panel;
- compact live telemetry cluster;
- current/next policy context;
- Expand control.

Navigation remains minimal; Rules and Settings can be reached without permanently consuming a side rail at this size.

### Expanded

Canonical size: **1280 x 800**, while remaining fluid above the Compact threshold and up to the work area.

Purpose: the complete PowerFlow cockpit.

The visual composition follows the supplied reference image:

- deep navy background with subtle glass/Mica depth;
- left navigation rail for Dashboard, Rules, and Settings;
- branded top header;
- horizontal power-state selector cards using semantic accent colors;
- large luminous trajectory/power graph as the main visual anchor;
- live telemetry/status panel to the right using real PowerFlow telemetry only;
- lower contextual panels for active rule/policy, recent behavior, and quick actions where existing app data supports them;
- restrained cyan edge glow and gradient accents rather than indiscriminate neon.

Expanded is not a separate dashboard tree. It is the same shell with additional information density and changed placement.

### Full Screen

Full screen remains an optional extension of Expanded, not a separate conceptual mode. It uses the same Expanded layout engine at greater density and available space.

## Interaction Contract

### Tray hover

Hovering the tray icon may reveal Glance after the existing bounded hover delay. The shell must use `WS_EX_NOACTIVATE` while it is a transient hover glance. Leaving both icon and shell hides it after the existing grace policy.

### Tray single click

A single left click becomes the primary PowerFlow interaction.

- If hidden: reveal and pin Glance at the tray anchor, then activate it.
- If transient Glance is already visible: pin and activate the same HWND without recreating it.
- A double click may advance directly to Compact as a convenience, but must not be required for ordinary use.

### Glance click

Clicking the Glance surface grows the same HWND to Compact. It must not close one window and open another.

### Compact Expand

Expand grows the same HWND to Expanded. Collapse reverses to Compact. A close/minimize-to-tray action shrinks/hides toward the last tray anchor rather than abruptly disappearing when motion is enabled.

### Rules and Settings

At Compact size, selecting Rules or Settings may grow the shell to Expanded automatically if their layout needs the space. The user returns to the same shell and prior Dashboard state.

## Motion Model

Motion must explain hierarchy, not decorate it.

### Window geometry

All state changes animate position and size together with `AppWindow.MoveAndResize`, not size alone. The tray icon/work-area rectangle is the geometric source for Glance and the origin used when growing toward Compact.

Target timing:

- Hidden -> Glance: 110-150 ms;
- Glance -> Compact: 160-200 ms;
- Compact -> Expanded: 180-240 ms;
- reverse transitions: equal or slightly faster.

Use one cubic ease-out family for growth and a complementary ease-in/out for collapse. No bouncing or elastic motion.

### Shared visual anchors

The following are persistent element instances wherever practical:

- current state/mode selector;
- primary telemetry cluster;
- trajectory control;
- current/next policy context;
- PowerFlow identity/header.

The layout engine changes their `Grid.Row`, `Grid.Column`, spans, padding, size, visibility density, and composition offsets as shell state changes. This preserves identity instead of cross-fading duplicate controls.

### Entering detail

Elements that only exist at higher density may fade/translate in after the window has completed roughly the first third of its growth. Shared elements move first; secondary detail follows. Exiting performs the reverse ordering.

### Reduced Motion

Respect Windows animation settings and `ReducedMotionOverride`. With reduced motion enabled, state/layout changes occur immediately or with a minimal opacity handoff; all information and interaction behavior remains identical.

## Architecture

### One shell window

`MainWindow` becomes the single PowerFlow shell window. It is created once and reused for hover, pinned Glance, Compact, Expanded, Full Screen, Rules, and Settings.

The separate `TrayHoverWindow` is removed after parity is proven.

### Presentation state

Replace the dashboard-only presentation enum with a shell enum:

```csharp
public enum PowerFlowShellState
{
    Hidden,
    Glance,
    Compact,
    Expanded,
    FullScreen
}
```

Transient/pinned behavior is orthogonal state, not another visual mode:

```csharp
public enum ShellActivationMode
{
    TransientNoActivate,
    PinnedActive
}
```

### Layout profile

Evolve `DashboardResponsiveLayout` into `PowerFlowShellLayout` returning a `PowerFlowShellLayoutProfile` for actual width, height, state, and navigation section. The profile owns density decisions such as:

- navigation rail visibility/width;
- mode-selector density;
- graph minimum/desired height;
- telemetry density;
- context panel visibility;
- header/logo density;
- padding/gaps/font scale;
- detail panel visibility;
- corner radius and chrome density.

Pure layout calculations remain testable without WinUI.

### Geometry transition controller

Create a focused `ShellTransitionGeometry` pure model that computes target rectangles and interpolated frame rectangles from:

- tray icon rectangle;
- monitor work area;
- current shell bounds;
- target shell state;
- normalized progress.

`MainWindow` owns the DispatcherQueue timer/composition application but delegates geometry math to the pure model.

### Tray host

`TrayIconHost` adds explicit single-left-click signaling. It continues owning icon placement discovery and context-menu commands. It must not create visual windows.

### App lifecycle

`App` owns one `_shellWindow`. Hover, tray click, secondary-instance dashboard signals, settings navigation, and preview all route through that shell. There must never be a `TrayHoverWindow` and `MainWindow` visible simultaneously because only the shell remains.

## Visual System

The supplied reference is the visual direction, not a demand to invent unsupported telemetry or controls.

### Palette

Use theme resources, not hard-coded colors in controls.

- canvas: near-black navy;
- elevated surface: blue-black/navy with subtle alpha depth;
- borders: desaturated blue-gray;
- primary accent: electric cyan;
- efficiency/saver accent: green/teal;
- balanced accent: cyan/blue;
- performance accent: orange/coral;
- auto accent: restrained purple;
- text: cool white with blue-gray secondary text.

### Surfaces

- 12-18 px corner radii depending on shell size;
- 1 px low-contrast borders;
- Mica where supported, with deterministic fallback brush;
- soft accent glow only on selected/active controls and graph highlights;
- no giant gradients behind ordinary text;
- no tiny text: 11 px is the absolute explicit-font minimum, with primary dashboard text larger.

### Graph

The trajectory graph remains the hero. It gains a polished reference-like treatment:

- subtle grid;
- cyan/teal/purple series from real telemetry/policy data;
- soft fill/area glow where supported;
- crisp NOW marker;
- hover detail card;
- time-range controls only at densities that can afford them.

No decorative fake data series are allowed.

### Power-mode selector

The selector visually echoes the reference state cards. At Expanded density it exposes Power Saver, Balanced, Performance, and Auto as distinct semantic cards. At Compact it uses a tighter horizontal presentation. At Glance it emphasizes only the current state plus an affordance to grow.

## README Contract

Rewrite `README.md` for a person discovering PowerFlow on GitHub. It is product documentation, not release playback.

Required sections:

1. **What PowerFlow is** — one strong paragraph with personality.
2. **Why it exists** — Windows power plans are useful but static/manual; PowerFlow adds adaptive behavior without turning monitoring into another workload.
3. **How it works** — Saver, Balanced, Performance, Auto/rules; hysteresis, hold windows, cooldown, game/app lifecycle.
4. **What makes it different** — tray-first, explainable state changes, low-overhead background continuity, real Windows power-plan switching, modern native UI.
5. **Screenshots** — current Glance and Expanded screenshots; Compact when it adds useful context.
6. **Install / build / run** — only truthful currently-supported paths. Do not claim a packaged installer or GitHub Release asset until one exists.
7. **Configuration / safety** — concise user-facing configuration location and what PowerFlow does/does not change.
8. **Status / limitations** — short beta statement, no internal acceptance transcript.
9. **AI note** — one brief note, approximately: “PowerFlow is vibe-coded with AI, but developed with test-driven discipline: behavior is specified in tests before changes are accepted.”

Explicitly remove from the README:

- machine name “reference-host”;
- HWNDs, capture mechanics, acceptance matrices, or PID evidence;
- test-count chest-thumping;
- red/green crash archaeology;
- internal commit/release forensic narration;
- “current candidate passed” sections.

Detailed engineering evidence remains in `docs/acceptance/`.

## Testing Strategy

TDD is mandatory for behavior changes.

### Pure tests

Add tests for:

- shell state target sizes and density profiles;
- geometry anchored to tray/work area;
- interpolation monotonicity and exact endpoints;
- layout continuity within Compact/Expanded resizing;
- Reduced Motion policy;
- tray single-click semantics;
- hover transient -> pinned promotion;
- Glance -> Compact -> Expanded state transitions;
- no duplicate window ownership path in `App` source contract.

### WinUI/source-contract tests

Verify:

- one shell XAML tree contains the Glance/Compact/Expanded content anchors;
- separate `TrayHoverWindow` is no longer instantiated;
- visible explicit fonts are never below 11 px;
- semantic colors come through theme resources;
- `AppWindow.MoveAndResize` is used by shell geometry transitions;
- Rules/Settings route through the same shell.

### Live acceptance

On reference-host, after the test suite is green:

1. Start tray-first and verify no visible shell.
2. Hover tray icon; verify one PowerFlow HWND at Glance size and no activation theft.
3. Single-click tray icon; verify the same HWND becomes pinned/active.
4. Click Glance; record same HWND throughout growth into Compact.
5. Click Expand; record same HWND throughout growth into Expanded.
6. Collapse back to Compact and hide toward tray; verify same HWND and expected final hidden state.
7. Resize Expanded manually across several widths; verify fluid reflow and readable text.
8. Exercise Rules and Settings.
9. Verify one PowerFlow process, no duplicate windows, no new Application/.NET/WER crash events.
10. Capture current Glance, Compact, and Expanded screenshots directly from the PowerFlow HWND for README use.

## Non-Goals

This UI pass does not:

- invent GPU/RAM/temperature telemetry PowerFlow does not already collect;
- add a new backend service;
- rewrite power-plan policy behavior;
- add unrelated system diagnostics;
- add a packaged installer unless separately approved;
- change the established remaining power-overhead/game-soak hardening work.

## Acceptance Criteria

The change is ready for visual review when:

- a single PowerFlow HWND performs Glance -> Compact -> Expanded transitions;
- single tray click opens/pins Glance;
- the Glance click grows that same HWND into Compact;
- Expand grows the same HWND into the reference-inspired cockpit;
- shared graph/state/telemetry elements maintain spatial continuity;
- Expanded appearance clearly reflects the supplied reference image without fake telemetry;
- Reduced Motion works;
- full automated suite and Release build are clean;
- live tray-to-expanded acceptance has zero new crash events;
- README reads as public product documentation and contains only the concise AI/TDD note described above;
- runtime/repo hygiene is clean before release publication.