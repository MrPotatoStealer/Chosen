# 20261031-083335: fame is seen here when building is not destroyed

Status: fixed
Regression: sprites, room-over-room
Regression check: no fire on the wall left of the gate (fire 711 under 59's floor until the building blows); seen in the recording's replay

## History

- 2026-10-31 08:33:35 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: Fire 711 (sector 59, a z-motion sprite sector: under its floor until the building blows) is tall enough to reach the yard's linked ceiling, so it counted as reaching through that portal and lost its floor/ceiling clip.
- 2026-10-31 Fix: SpriteRenderer.ZClipOf: a sprite is always clipped to its own sector's floor and ceiling, except a plane that is a portal of that sector.
