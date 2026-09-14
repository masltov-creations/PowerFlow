# PowerFlow Design

Date: 2026-09-08
Status: Approved interaction concept; implementation not started
Working title: PowerFlow

## Purpose

PowerFlow is a tray-first Windows power-plan controller inspired by eliosteva/PowerPlanManager, rebuilt as a modern, low-overhead native Windows utility. Its job is to keep reference host in Power Saver during ordinary use, promote to Balanced under sustained CPU demand, and latch into High Performance for games or explicitly tagged heavy applications.

The utility must save more power than it consumes. Background overhead is therefore a product requirement, not an optimization to defer.

## Product principles

1. Power Saver is the normal resting state.
2. CPU spikes alone do not cause plan flapping; promotions use persistence and hysteresis.
3. Gaming is a latched state. Once a game is recognized, PowerFlow must not wind performance down because the game is idle, paused, in a menu, GPU-bound, loading, or temporarily low-utilization.
4. While gaming, PowerFlow becomes extremely quiet: normal telemetry polling and dashboard animation stop when the dashboard is closed.
5. Manual High Performance is also latched until the user explicitly releases it.
6. The app changes active power plans; it does not continuously rewrite the contents of those plans.
7. Every automatic transition must have an explainable cause.
8. The dashboard is motion-rich when open and nearly nonexistent when closed.

## State model

### Power Saver
Default state for ordinary desktop use.

Promotion to Balanced requires sustained CPU demand rather than a transient spike. Initial target: CPU demand above a configurable threshold for roughly 3-5 seconds. Exact thresholds are calibration values, not architectural constants.

A recognized game or explicit Performance rule skips directly to High Performance.

### Balanced
Used for sustained non-game CPU work.

Promotion to High Performance occurs when a game is detected or an explicit Performance app rule becomes active.

Demotion to Power Saver requires a quiet period with hysteresis, initially around 20-30 seconds, to prevent oscillation.

### High Performance / Game Latch
Entering a recognized game creates a game latch. The latch remains active until the tracked game process set is actually gone.

Low CPU/GPU utilization must never clear the latch. Loading screens, pause menus, cinematics, shader compilation waits, background matchmaking, and GPU-bound gameplay are all valid game states.

While latched:
- active plan remains High Performance;
- normal CPU/GPU telemetry polling is suspended when the dashboard is closed;
- no idle-based demotion logic runs;
- the controller tracks only lightweight process/game lifecycle signals;
- dashboard animation is not rendered while the dashboard is closed;
- tray state communicates `Performance Locked - Game`.

When all tracked game processes exit, PowerFlow enters a short cooldown, then returns to Balanced. Normal quiet-state evaluation can later return the system to Power Saver.

### Manual latch
A user-selected High Performance override behaves like Game Latch but has no automatic release condition. It remains active until explicitly released.

## Game detection

Use a hybrid model:

1. Explicit per-app rules have highest confidence and priority.
2. Known launchers/process relationships may identify the spawned game executable.
3. Outside Game Mode only, a conservative heuristic may combine foreground/full-screen status and GPU activity to suggest an unlisted game.
4. Once a game is latched, track its actual process lifecycle rather than utilization.

Process trees must be followed across launcher-to-game handoff where practical. A launcher remaining open after the actual game exits must not keep High Performance latched indefinitely unless explicitly configured to do so.

False negatives are preferable to intrusive constant polling, but the UI must make it easy to promote the current foreground application to a remembered game rule.

## Background-overhead budget

PowerFlow must be designed around measurable budgets:

- Dashboard closed, ordinary desktop: target effectively negligible CPU use, with slow/coalesced sampling only where needed for Balanced promotion detection.
- Game latched: no normal CPU/GPU telemetry polling; event-driven/process-exit tracking only.
- No 100-250 ms background timers.
- No continuous animation or hidden UI rendering while the dashboard is closed.
- Avoid heavyweight browser/Electron runtime.
- No permanent WMI query loops. If WMI/ETW/process notifications are used, prefer event subscription over periodic enumeration.
- Logging is bounded and buffered; no high-frequency disk writes.

Acceptance testing must compare PowerFlow-on versus PowerFlow-off CPU package power and privileged CPU usage on reference host. The controller is not acceptable if its monitoring materially erodes the power savings it is intended to create.

## Architecture

### Native shell
.NET with WinUI 3 / Windows App SDK is the preferred implementation. It provides a native Windows visual surface and composition animation without an embedded browser runtime.

The app is a single tray-first user process unless implementation evidence proves a separate service is necessary. Avoid a service by default.

### Policy Engine
Pure state-machine logic responsible for Power Saver, Balanced, Game Latch, Manual Latch, cooldown, hysteresis, and transition reasons. It must be independently unit-testable with synthetic telemetry/process events.

### Power Plan Controller
Thin Windows integration layer that reads available plans and activates configured plan GUIDs. It never modifies plan settings during normal operation. It verifies the resulting active plan after every change.

### Activity Monitor
Low-rate/coalesced CPU demand monitoring used only outside a game latch. Package-power/frequency telemetry is primarily for the open dashboard and diagnostics, not required for the core switching loop.

### Game Lifecycle Monitor
Detects candidate game starts and follows the selected process set. Once latched, process lifecycle becomes the release signal. The design must avoid repeated process enumeration while latched where event-driven exit observation is possible.

### Rule Store
Small local configuration file containing:
- mapped GUIDs for Power Saver, Balanced, and High Performance;
- explicit app/game rules;
- thresholds and cooldowns;
- manual/user preferences;
- UI preferences.

Use a human-readable, versioned format such as JSON. No database.

### Tray Shell
Always-available interaction surface:
- current power state;
- reason/latch status;
- quick manual Power Saver / Balanced / High Performance choices;
- release manual latch;
- open dashboard;
- exit.

Tray icon/state changes must be subtle and legible, not animated continuously.

### Motion Dashboard
The full dashboard is created/opened on demand. Its visual language is a living horizontal energy flow across three destinations: Power Saver -> Balanced -> Performance.

The current state has visual gravity. Approaching thresholds should be represented as energy gathering rather than flashing gauges. On a promotion, motion should visibly travel into the next state. During cooldown, energy should drain smoothly toward lower-power states.

Primary dashboard information:
- active state and transition reason;
- live CPU demand;
- CPU package watts when available;
- CPU frequency;
- current foreground/triggering application;
- latch/cooldown status;
- short transition history.

Secondary views:
- app/game rules;
- thresholds and timing;
- plan mapping;
- diagnostics/overhead;
- startup behavior.

Visual rules:
- motion-flow oriented, not card-wall oriented;
- smooth inertia and interpolation;
- no twitchy meters;
- no decorative polling while hidden;
- support Reduced Motion by replacing travel animations with gentle crossfades/state changes;
- UI remains understandable without animation.

## Transition precedence

Highest to lowest:

1. Manual High Performance latch
2. Game latch / explicit Performance rule
3. Explicit Balanced rule
4. Sustained CPU-demand promotion
5. Quiet-state demotion

A higher-priority active condition prevents lower-priority rules from winding the state down.

## Failure and recovery behavior

- If plan activation fails, retain the previous known state, report the failure, and do not pretend the transition succeeded.
- On startup, discover the actual Windows active plan before evaluating policy.
- After an abnormal app restart, do not blindly force Power Saver. Re-evaluate active game processes/rules first and prefer Balanced as the safe neutral recovery state if intent is ambiguous.
- If a game process cannot be confidently followed after launcher handoff, keep the latch for a bounded grace period and surface the uncertainty rather than immediately dropping performance.
- Configuration writes are atomic with last-known-good fallback.

## reference host-specific safety constraint

Power Saver on reference host is currently being tested for a possible long-idle hard-freeze. PowerFlow must not be treated as production-ready until the underlying Power Saver stability question is resolved. During development/testing, a configuration flag must allow Balanced to be used as the resting state so utility testing does not repeatedly expose the machine to a known-suspect power state.

PowerFlow is not intended to mask a BIOS/AGESA/C-state stability defect.

## Startup

Prefer normal per-user Windows startup rather than a privileged scheduled task. The app should start minimized to tray, instantiate no dashboard window, verify configured plans, establish event subscriptions, and then settle into its low-overhead controller loop.

No console windows or startup popups.

## Testing and acceptance

### Policy tests
Deterministic tests cover:
- short CPU spike remains Power Saver;
- sustained CPU load promotes to Balanced;
- quiet hysteresis demotes Balanced to Power Saver;
- game start jumps directly to High Performance;
- game CPU utilization falling to zero does not demote;
- pause/menu/loading intervals do not demote;
- child/spawned game process handoff preserves latch;
- actual game exit releases latch and enters cooldown;
- manual Performance remains latched indefinitely until release;
- precedence rules cannot be overridden by lower-priority conditions.

### Integration tests
- verify real Windows plan GUID discovery and switching;
- verify state after each plan activation;
- verify process-exit/game-lifecycle tracking;
- verify startup/exit behavior;
- verify no orphan helper processes;
- verify configuration recovery after simulated corruption.

### Performance tests on reference host
Measure against a controller-off baseline:
- CPU package power;
- total CPU utilization;
- privileged CPU utilization;
- wakeups/timer activity where available;
- disk writes;
- game frame-time impact.

Test separately with dashboard closed, dashboard open, and Game Latch active. Game-latched/closed mode should be the quietest mode of the application.

### UI acceptance
The dashboard must visually communicate all three of these without reading a log:
1. where the system is now;
2. why it is there;
3. whether it is gathering momentum toward another state, cooling down, or latched.

## Non-goals for v1

- Editing hidden Windows processor settings.
- Creating bespoke replacement power plans unless explicitly requested later.
- Automatic BIOS tuning.
- Cloud sync/account system.
- Historical analytics database.
- Per-core scheduler manipulation.
- Fan or RGB control.
- Overclocking/undervolting.

## Inspiration and differentiation

The original PowerPlanManager automatically selects Power Saver, Balanced, or Performance from running applications/user activity and can map custom plans. PowerFlow keeps the useful three-state concept but separates plan selection from plan mutation, adds hysteresis, game latching, explicit overhead constraints, explainable transitions, and a modern motion-oriented native tray/dashboard experience.
