# 20261019-162448: light is not replicated through portal

Status: fixed
Regression: room-over-room, dynamic-lights

## History

- 2026-10-19 16:24:48 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: a light between the two portal planes (the yard ceiling at 32, the upper floor at 48) was on neither room's side, so it got no copy in the linked room
- 2026-10-19 Fix: lights within 24 units behind a portal plane are copied too (both ways), at full energy
