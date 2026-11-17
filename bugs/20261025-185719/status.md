# 20261025-185719: very messed up rendering from inside the lift

Status: fixed
Regression: movers, performance
Regression setup: send s8 on | 3
Regression check: from inside the cabin the corridor north of the open door shows, no lava room

## History

- 2026-10-25 18:57:19 reported in maps/e3m5.map
- 2026-10-25 fixed. Cause: Same phantom ring wall: in the sectors' occluder it covered the doorway from inside the cabin, so the corridor beyond could be culled (resolution dependent: the 3440x1440 view lost it, 1280x720 replays didn't) and the lava room's portal view showed through.
- 2026-10-25 Fix: Same fix: 'mov' walls are out of the drawn walls and the occluder.
- 2026-10-25 reopened: Not fixed by the mov walls: the movers regress batch reproduced it once (baseline.jpg: the lava room copy of link 30 drawn in the cabin doorway), two reruns didn't. Intermittent, render side (PortalRenderer's copy of the far side / its clip test); the phantom-wall fix stays.
- 2026-10-26 fixed. Cause: The sectors' occluder (LiveRenderer) was never freed on a level change or restart: the last level's walls kept culling this level's rooms. After E1M1, E3M5's corridor north of the lift was culled and the lava room's portal copy behind it showed (the regress batch starts on E1M1; alone it never failed).
- 2026-10-26 Fix: The occluder is freed with the level's sector meshes in LiveRenderer.Setup.
