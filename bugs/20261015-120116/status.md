# 20261015-120116: no collision on the round sliding door, interaction is not working on it as well.

Status: fixed
Regression: movers, collision
Regression check: the round door has collision and can be used

## History

- 2026-10-15 12:01:16 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Rotating door (sector 26, type 617) had no collision and could not be pushed: moverPlace() set the new angles after setorigin(), and DarkPlaces computes a rotated brush model's collision box at link time from the angles, so the box was stale.
- 2026-10-15 Fix: qc/server/triggers.qc moverPlace(): set angles before setorigin(). Tested: door blocks, pushing it rotates it open (secret with the TNT case).
