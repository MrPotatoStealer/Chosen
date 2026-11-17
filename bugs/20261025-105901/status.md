# 20261025-105901: invisible collision here

Status: fixed
Regression: collision
Regression check: walk east from the spot at y 935 (and 952): the player passes x -428 into the alcove instead of stopping

## History

- 2026-10-25 10:59:01 reported in maps/e2m5.map
- 2026-10-25 fixed. Cause: A converter strip (blockers) along the wall between room 8 and pedestal sector 20: the upper wall from 20's ceiling (108) to 8's (160), solid, 0.5 thick. Room 680 (floor 64) and the alcove 708 lie over room 8 with their open space across that band, so it was an unseen wall at head height there; room 8's walkers (floor -24) can never reach it.
- 2026-10-25 Fix: convert_map blockers: a strip more than STRIP_REACH (96) above its own room's floor and less above the other room's floor has no collision (blood_flat), as one standing on the other room's floor already had.
