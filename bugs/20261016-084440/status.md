# 20261016-084440: standing in this area should not be possible, should remain crouched?

Status: fixed
Regression: collision
Regression check: releasing crouch under the low ceiling keeps the eye under it

## History

- 2026-10-16 08:44:40 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: Blood stands the player up when crouch is released (no ceiling test) but viewDrawScreen clamps the eye 8<<4 under its sector's ceiling; our client had no clamp, so the eye went above the 48-unit ceiling.
- 2026-10-16 Fix: Client clamps the eye half a unit under the ceiling / over the floor (trace from the body, link planes non-solid), with a nearer clip plane while clamped.
