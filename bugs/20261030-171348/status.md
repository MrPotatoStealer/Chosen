# 20261030-171348: hole in geo herew

Status: fixed
Regression: movers
Regression setup: b3d_fixblood 0; send 115 off; send 100 off | 5
Regression check: after the ice block slides off: water where it stood, no hole (also with transparent water)

## History

- 2026-10-30 17:13:48 reported in maps/e2m1.map
- 2026-10-30 fixed. Cause: The ice block (114, a whole sliding sector, a rigid mover in the live lake 125) leaves its hole in the lake as built: Build drags the hole's corners along, the live sweep kept the lake's loop round its stored place, so its floor had a hole to the sky there once the block had gone (black under transparent water)
- 2026-10-30 Fix: World.FindLiveRigid: a live sector's loop round a rigid mover, its corners still, is left out of the sweep (LiveSector.RigidHole); the mover's outline cuts it out where it is
