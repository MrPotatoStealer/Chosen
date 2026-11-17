# 20261029-094607: projectiles like napalm crossing portal produces a strange double projectile seen from the side?

Status: fixed

## History

- 2026-10-29 09:46:07 reported in maps/e1m1.map
- 2026-10-29 fixed. Cause: the Build-way sky never hides sprites: the napalm ball gone through E1M1's sky link was drawn in the room over it as well as its portal copy
- 2026-10-29 Fix: only sprites of sectors the walk reaches without a link mark the stencil (SectorVisibility.DirectlySeen); the others the sky hides
- 2026-10-29 fixed. Cause: the renderer blended a sprite across a link between game frames only when it guessed the crossing, and missed it where the link's offset is shorter than the sprite's frame (E1M1's sky link: 32 units, a napalm ball moves ~40): drawn between the two rooms for a frame
- 2026-10-29 Fix: CheckLinkCross moves PrevOrigin by the link's offset as well, as NBlood's viewCorrectSpriteInterpolateOffsets; CollisionWorld.Interpolate no longer guesses crossings
