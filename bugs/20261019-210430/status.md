# 20261019-210430: this room over room is still not fixed,

Status: fixed
Regression: stacked-rooms, sector-lighting
Regression setup: nomonsters | 0.2

## History

- 2026-10-19 21:04:30 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: Sector 133 under 154 (upper floor 32 below the lower ceiling 56): the upper floor is a light-effect skin, which wasn't hidden from the lower room, and the height-based rule failed anyway (the camera in 134 was above 32).
- 2026-10-19 Fix: Side by the camera's room instead of its height (StackedRooms), and the upper floor's light-effect skin is tagged too.
