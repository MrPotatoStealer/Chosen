# 20261019-122850: fire is not animating, prerender textures like in darkplaces or convert it to an actual shader. Check NBlood how it's originally done

Status: fixed
Regression: sprites

## History

- 2026-10-19 12:28:50 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: tile 2342 is Blood's procedural fire (fire.cpp), redrawn every 3 ticks; Godot showed one static frame
- 2026-10-19 Fix: Render/ProceduralFire ports fire.cpp (cellular frame, cool table, seed rows, RFIRE.CLU exported to data/rfire.clu) and updates a live texture every 3 ticks of the 120 Hz clock; walls and sprites of tile 2342 draw it
