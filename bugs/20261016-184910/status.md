# 20261016-184910: OutOf Bounds collision jump

Status: fixed
Regression: room-over-room, collision
Regression setup: send s30 on | 4
Regression check: fall back through the coffin portal near its edge: no out of bounds

## History

- 2026-10-16 18:49:10 reported in maps/E1M1.bsp
- 2026-10-18 fixed. Cause: Falling back through the coffin's portal 4 units from its edge put the player's box (radius 12) overlapping the solid behind the coffin's side wall. DarkPlaces doesn't block a box that starts inside a brush, so the next moves went through the wall and out of the map. The coffin's side walls are blocking (cstat 1) but, as walls of a sliding sector, had no clip slab, so the 4-unit strips behind them also gave the player their floor and ceiling (height flip-flopping).
- 2026-10-18 Fix: ClipMove pushes an actor whose box overlaps solid out first (Build's pushmove) and otherwise only lets it move to a free spot. Lid walls that only change length (the vertex slides along the wall) get blocking slabs over their full travel. Verified with the recording: the free-running replay keeps the player inside the coffin.
