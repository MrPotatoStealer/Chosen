# 20261101-133626: more sprites disappearing at edge of screen

Status: fixed
Regression: sprites

## History

- 2026-11-01 13:36:26 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: Same as 20261101-133141: E1M1 ceiling lamps tied with the ceiling when looking level.
- 2026-11-01 Fix: SpriteRenderer: the nearer of lifted off the surface and pulled towards the eye. Both lamps show at the report view, as in NBlood.
