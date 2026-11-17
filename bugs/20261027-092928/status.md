# 20261027-092928: no collision on door

Status: fixed
Regression: collision
Regression check: walk north into the swing door from the south (e1m4-door): it blocks, as the brushes do

## History

- 2026-10-27 09:29:28 reported in maps/e1m4.map
- 2026-10-27 fixed. Cause: Room 397 behind the swing door 233 is a live sector; Build's collision took its walls from the live shape (stored corners) before the door's mover, so the walls it shares with the turning door stayed at the stored, open place and the closed door let the player through
- 2026-10-27 Fix: WallNow takes a wall of a whole moving sector, or facing one, where the mover has it before a live sector's shape (TranslateSector drags those points)
