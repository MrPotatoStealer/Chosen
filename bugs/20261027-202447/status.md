# 20261027-202447: hole in geo after moving

Status: fixed

## History

- 2026-10-27 20:24:47 reported in maps/e4m2.map
- 2026-10-27 fixed. Cause: the stairs 205-210 over E4M2's turning room 33 joined its live cluster by plan alone (and by shared corner coordinates), so its turn cut holes in them
- 2026-10-27 Fix: plan_live: live sectors from DragPoint's walls (drag_walls), and the region takes in a sector only where its heights meet the live sector reaching it
