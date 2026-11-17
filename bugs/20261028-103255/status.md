# 20261028-103255: here is the part where flickering happening

Status: fixed
Regression: water, room-over-room
Regression check: crouch-swim on the top flooded step (e3m3-stairs, hold crouch): the room above never drops out

## History

- 2026-10-28 10:32:55 reported in maps/e3m3.map
- 2026-10-28 fixed. Cause: Same as 20261026-180722: a tick with the body in the underwater step and its origin above the water plane drew the view in the void over the water
- 2026-10-28 Fix: PlayerView.LinkViewOrigin by the body's Build sector (Blood's viewDrawScreen)
