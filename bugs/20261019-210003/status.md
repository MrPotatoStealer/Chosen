# 20261019-210003: strange geo bug

Status: fixed
Regression: movers

## History

- 2026-10-19 21:00:03 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: Sector 190's sliding pane (a 614 sweep) moves 48 units into a wall pocket only 16 deep. Build squeezes the wall; the converter's rigid panel came out 16 units into the next room (the slanted plank).
- 2026-10-19 Fix: Sweep panels get clipMin/clipMax (their area when shut); the mover shader draws them only inside it.
