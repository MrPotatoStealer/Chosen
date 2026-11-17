# 20261031-090743: can't break window, and window is missing its collision

Status: fixed
Regression: masked-walls, collision
Regression check: shoot the train window: it shatters (shards, gone) and only then lets you through

## History

- 2026-10-31 09:07:43 reported in maps/e1m3.map
- 2026-10-31 fixed. Cause: Shooting the train window (gib wall 1042/1913) broke it in the game (x-state 1, blocking cleared in the lookups' copy), but SetupGibWallState left the board's walls as they were: the native world kept drawing the glass, and its shards spawned at the converted brush's empty bounds. The player walked through the glass that was still drawn.
- 2026-10-31 Fix: SetupGibWallState sets the board's wall flags too; the native world rebuilds the wall's sectors (WallVisibilityChanged -> NativeWorld.WallChanged); GibWall ported (the pieces over the wall's face, NBlood gib.cpp).
