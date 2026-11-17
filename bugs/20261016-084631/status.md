# 20261016-084631: can't shoot thru the window?

Status: fixed
Regression: masked-walls, collision
Regression check: break the window and shoot through it

## History

- 2026-10-16 08:46:31 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: The glass window is a kWallGib pair with cstat 1; the converter's blocking-wall slab stayed after it broke (Blood's SetupGibWallState clears cstat 1). Shots pass the glass once broken.
- 2026-10-16 Fix: Blocking-wall slabs skip breakable glass; either side of the pair can carry the gib x-wall.
- 2026-10-16 fixed. Cause: The glass window is a kWallGib pair with cstat 1; the converter's blocking-wall slab stayed after it broke (Blood's SetupGibWallState clears cstat 1). Shots did pass the broken glass, but the room behind is a light-effect sector whose world faces were caulk behind non-solid skins, so hits there had no surface (no bullet holes or sounds).
- 2026-10-16 Fix: Blocking-wall slabs skip breakable glass. World faces behind light-effect skins use solid unseen blood_fxhide/NNNN shaders named after the tile; texToTile parses palette variants and blood_oneway/blood_fxhide.
