# 20261027-093607: can we make pickups always use the sectors current lightning now?

Status: fixed

## History

- 2026-10-27 09:36:07 reported in maps/e1m8.map
- 2026-10-27 fixed. Cause: Sprites took a shade fixed at their first draw from the floor face under them, plus a light-effect sector found by position
- 2026-10-27 Fix: Sprites shade as viewProcessSprites: their Build sector's sprite shade (ceiling's under a sky, else floor's, with the tile's) plus its light effect now (World.SectorSpriteShade, PeekSpriteBSector)
