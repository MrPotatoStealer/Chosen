# 20261101-191449: small strip of missing geo here when door is rotating

Status: fixed
Regression: movers
Regression check: bugs.py motion holds this door against the unculled exact picture: no strip of the room behind cut away

## History

- 2026-11-01 19:14:49 reported in maps/e3m5.map
- 2026-11-01 fixed. Cause: The visibility walk measures what an opening lets through from where its corners are now, and only a sector moving whole counted as moving. E3M5's turning door drags the corners of the sector round it, which is drawn between the last game frame's place and now: a strip of the room behind was cut by its window (r_skyfaces 0), widest at the start of the blend. The motion test missed it: its exact picture was cut the same way.
- 2026-11-01 Fix: SectorVisibility: a wall with a corner moved since the last game frame (CornersMoved) is taken as open whole. Motion test: the exact picture is drawn with no culling, and this door is a case: BROKEN (0.188%) without the fix, 0.020% with it.
