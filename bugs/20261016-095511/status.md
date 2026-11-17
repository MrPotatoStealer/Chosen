# 20261016-095511: coffing lid is a bit to high up for some reason, should be flush

Status: fixed
Regression: room-over-room, movers
Regression setup: send s30 on | 4

## History

- 2026-10-16 09:55:11 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: The lid slab's face seen through the coffin's room-over-room portal was lifted 2 units off the plane to clear the portal clip plane, so it sat above the opening.
- 2026-10-16 Fix: Lift 0.1; the client sets r_water_clippingplanebias 0 itself (it came only from a saved config).
