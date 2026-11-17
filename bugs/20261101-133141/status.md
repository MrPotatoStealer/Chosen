# 20261101-133141: sprite getting cut off near edge of screen

Status: fixed

## History

- 2026-11-01 13:31:41 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: A flat sprite took its depth lifted along its surface normal; with the view along the surface that changes nothing, so it tied with the wall or ceiling and was lost.
- 2026-11-01 Fix: SpriteRenderer: the nearer of lifted off the surface and pulled towards the eye.
