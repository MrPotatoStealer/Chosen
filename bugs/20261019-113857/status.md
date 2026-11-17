# 20261019-113857: looks like there is a missing texture here on the side, we are seeing the wall behind it. Difficult to see this since they are using the same texture.

Status: fixed
Regression: room-over-room, movers
Regression setup: send s30 on | 4

## History

- 2026-10-19 11:38:57 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the coffin's sliding lid has stretching walls around its opening (blood_stretchwall); Godot drew none, so through the stairwell's portal the opening's sides were missing
- 2026-10-19 Fix: Render/StretchRenderer (same fix as 20261019-113058)
