# 20261019-122928: sprites appearing and disappearing based on distance

Status: fixed
Regression: room-over-room, sprites

## History

- 2026-10-19 12:29:28 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: sprites took their facing from the nearer of the real eye and every drawn portal's virtual eye; a far portal's virtual eye could be nearer to a sprite elsewhere, so one-sided wall sprites were judged from behind their wall and hidden, depending on distance
- 2026-10-19 Fix: only portals whose footprint the eye stands under or over (the eye's room, as the DarkPlaces client did) orient sprites
