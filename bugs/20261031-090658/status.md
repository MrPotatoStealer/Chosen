# 20261031-090658: incorrect showing of doors

Status: fixed
Regression: stacked-rooms
Regression check: no door picture in room 31's ceiling (sliding door 159 of the room above)

## History

- 2026-10-31 09:06:58 reported in maps/e1m3.map
- 2026-10-31 fixed. Cause: Sliding door 159 (type 614) lies in the room above (158/160, floor 32) over room 31 (ceiling 56): Build's shared space. FindOverlaps leaves every moving sector out (a door panel stored open over the next room isn't shared space), so nothing hid 159's walls between 32 and 56 from 31.
- 2026-10-31 Fix: SectorMap.FindOverlaps: a moving sector shares the ground with a room that one of its non-moving neighbours (across a wall) shares the ground with.
