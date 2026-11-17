# 20261026-181057: hitting some strange collision here with the napalm

Status: fixed

## History

- 2026-10-26 18:10:57 reported in maps/e3m3.map
- 2026-10-26 note: 2026-10-26: missiles now move on Build's sectors (World.BuildClip MoveMissileSectors: clipmove + getzrange), not the converter's brushes, so the slab at the stacked link 15/16 no longer stops the napalm: please check.
- 2026-10-28 fixed. Cause: Missiles collided with the converter's brushes (a slab at stacked link 15/16)
- 2026-10-28 Fix: Missiles move on Build's sectors (MoveMissileSectors), confirmed in play
