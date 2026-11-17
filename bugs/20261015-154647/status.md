# 20261015-154647: make the grave open on start like it should

Status: fixed
Regression: movers

## History

- 2026-10-15 15:46:47 reported in maps/e1m1.bsp
- 2026-10-15 note: Question for the reporter: the converted grave is in Blood's ON (open) state: the pit under sector 28 is open and the part under sector 27 keeps its lid, as in the map data. In Blood the lid also animates open 0.6 s after level start (switch sprite 51 listens on channel 7 = level start, sends 102 to the sliding sectors 28/30, which only move one edge and are not animated by the converter yet). Need to confirm what 'open' should look like.
- 2026-10-15 fixed. Cause: Same root cause as 20261015-161005: the grave lid/pit sectors 28/30 are deforming slide doors opened by the level-start switch (rx 7 -> tx 102); they were kept static in the open state, and the grave floor rise (offFloorZ->onFloorZ) was ignored.
- 2026-10-15 Fix: Lid parts + Z parts for marked slide sectors (see 20261015-161005). At start the player is in the closed grave; the lid slides open and the floor lifts the player 24 units.
