# 20261023-200849: missing part when opening door

Status: fixed
Regression: movers, geometry
Regression setup: send 224 off | 8
Regression check: with the cars 84/88/92 slid to OFF (over the doorway in Build's impossible space) open the door (send 102): its floor stays whole

## History

- 2026-10-23 20:08:49 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: The sliding sectors 84/88/92 at OFF lie over doorway 71/72 in Build's impossible space (no wall between); they were taken as rigid movers of the live region and cut the door's area away (and their solids ran to the map's top and bottom)
- 2026-10-23 Fix: Rigid movers only when bordering a live sector (convert_map live_rigid, liveRigid key read by FindLiveRigid)
