# 20261030-134403: throwing hand into water and sprite got duplicated, the one above water can't be attacked

Status: fixed

## History

- 2026-10-30 13:44:03 reported in maps/e3m3.map
- 2026-10-30 fixed. Cause: Sprites reaching through a portal plane are drawn unclipped (their parts meet); water links drawn as portals counted too, so a hand swimming 4 units under E3M3's flooded stairs was copied whole onto the surface, standing over the water (it is in the underwater room: not hit from above, as in Blood)
- 2026-10-30 Fix: ReachesThrough and UnderOwnPortal only for converted portals (tile 504): a copy through see-through water is clipped at the surface; water copies only of the far room's own sectors (FarRoom)
