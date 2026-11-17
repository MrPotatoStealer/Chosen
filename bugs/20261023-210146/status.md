# 20261023-210146: duplicate doors here flickering

Status: fixed
Regression: movers, stacked-rooms
Regression setup: send 102 | 2
Regression check: both doors open: no second door face flickering in the doorway

## History

- 2026-10-23 21:01:46 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: The stacked-room band strips (convert_map blockers) were made for live door 72's walls too: its zero-width folds over door 71 got a static door face that z-fought with the live one
- 2026-10-23 Fix: blockers skip live sectors' walls (the live renderer draws them, hidden by stacking)
