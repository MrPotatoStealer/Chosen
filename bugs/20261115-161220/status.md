# 20261115-161220: sprite on vent looks to be offset

Status: fixed
Regression: sprites
Regression setup: cam 2887.82 -2776.23 318.09 4.09 8.25 | 2
Regression check: the fan sits in its box on the wall

## History

- 2026-11-15 16:12:20 reported in maps/sevict.map
- 2026-11-15 fixed. Cause: The loader gave every sprite cstat 128 (the picture centred on its z), as all of Blood's own maps have it; Eviction's fan (sprite 2280, tile 2548, cstat 337) is stored without it, standing on its z, so it was drawn half its height (16 units) too low under its box.
- 2026-11-15 Fix: Sprites keep their cstat as stored (World.Native.SpawnNativeSprite); SpriteRenderer places a face or wall sprite's bottom on its z when it isn't centred (voxel models of Refreshed Supply's kind too).
