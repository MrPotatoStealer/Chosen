# 20261018-154121: sprite is not rotating with sector

Status: fixed
Regression: carried-sprites
Regression check: turntable: sprites rotate with it, also after destroying some

## History

- 2026-10-18 15:41:21 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Carried sprites were a linked list through the sprites (moveSprites/moveNext). Destroying a carried thing (barrels and gibs on the turntable) freed its entity; once DarkPlaces reused it, the chain ended there and the wheels behind it stopped following the turntable.
- 2026-10-18 Fix: Movers find their carried sprites with findchainentity(moveWithEnt) each time. Verified with the recording (reproduced with 0 differences before the fix).
