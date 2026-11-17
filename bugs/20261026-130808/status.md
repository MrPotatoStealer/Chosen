# 20261026-130808: invisisble collision

Status: fixed
Regression: collision
Regression setup: nomonsters | 0
Regression check: walk from the spot towards the bar's west wall: no invisible wall

## History

- 2026-10-26 13:08:08 reported in maps/e3m1.map
- 2026-10-26 fixed. Cause: The turning panel 245 made room 246 (over the bar 381) a live sector; its live floor solid filled down through the bar, which isn't live (rooms sharing space): an invisible wall
- 2026-10-26 Fix: 245 moves whole (the same converter rule as the counter): a rigid 617 mover, 246 not live
