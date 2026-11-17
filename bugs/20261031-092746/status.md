# 20261031-092746: human at start got stuck, it should collide with the train

Status: fixed
Regression: carried-sprites, movers
Regression check: the human on the tracks comes down to the train and bursts there about 5 s in, then the next two props in turn

## History

- 2026-10-31 09:27:46 reported in maps/e1m3.map
- 2026-10-31 fixed. Cause: Not a monster: the human on the tracks is prop 918 (thing 417, cstat 8192) of the sliding sector 88. It rides out of 88's own walls (they stay at y 3792) towards the train at y 3168, and sector 88's tx 224 blows it up there. NBlood's TranslateSector never changes a sprite's sectnum; the game followed the sprite's position, so at y 3792 it left sector 88's list and stopped moving.
- 2026-10-31 Fix: TranslateSector: a sprite flagged to move with its sector (8192/16384) stays in that sector wherever this puts it (SectAt follows the move), as Blood's sectnum does.
