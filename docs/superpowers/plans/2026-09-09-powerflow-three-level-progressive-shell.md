# PowerFlow Three-Level Progressive Shell Plan

**Date:** 2026-09-09

## Product contract

PowerFlow has one visual instrument expressed at three connected disclosure levels:

1. **Hover glance — 320 x 176**
   - non-activating tray hover;
   - current state + mode, one compact telemetry line, mini trajectory, next action;
   - no card wall and no independent visual model;
   - click opens the compressed dashboard.

2. **Compressed dashboard — 760 x 440 (default open)**
   - same NOW/state/trajectory/policy anchors as hover, enlarged rather than redesigned;
   - readable 11 px minimum primary UI text;
   - trajectory remains dominant but uses a compact graph height;
   - mode controls and policy rails stay operational;
   - secondary context stays hidden until hover/click;
   - explicit expand affordance.

3. **Expanded cockpit — 1120 x 720**
   - same trajectory remains visually anchored;
   - richer range/detail/context becomes visible around it;
   - expanded context is a horizontal rail, not a return to a card wall;
   - Rules and Settings are fully available;
   - explicit compress affordance.

## Evolution and motion

- Opening from tray starts compressed.
- Expanding/compressing animates window size and content opacity/offset over a short bounded transition.
- Manual resize maps onto the same two dashboard modes rather than creating arbitrary layouts.
- Reduced Motion removes transition travel/animation but preserves the state change.
- Hover uses the same trajectory projection and semantic brushes as dashboard.

## Breakpoints

- dashboard width < 920 or height < 580 => Compressed
- dashboard width >= 920 and height >= 580 => Expanded
- explicit toggle resizes to canonical mode size.

## Telemetry semantics

- Keep Windows-reported average processor frequency as a diagnostic metric only if its semantics are explicit.
- Verify behavior under load before deciding whether to relabel or replace it.

## Acceptance gates

- source-contract tests prove the three canonical sizes and default compressed open;
- graph minimum height is presentation-controlled, not hard-coded to expanded height;
- hover footprint is 320 x 176 and removes the separate three-column metric band;
- expanded-only context is absent in compressed mode;
- resize/toggle code honors Reduced Motion;
- all existing tests remain green;
- live run: hover small, compressed opens, expanded grows from same anchors, compress returns cleanly, close returns to tray;
- no unexpected foreground console/CMD windows.