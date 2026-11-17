# 20261016-095604: player pushing against the rotatign door collider prevents it from moving

Status: fixed
Regression: movers, collision
Regression check: stand in the rotating door's way: it still turns

## History

- 2026-10-16 09:56:04 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Our DoorBusy applied VDoorBusy's blocked/wait logic to sliding and rotating doors; HDoorBusy/RDoorBusy (TranslateSector) never stop for sprites.
- 2026-10-16 Fix: H/R doors always move; actors left inside are pushed to the nearest free spot (pushmove) or take Blood's crush damage (500<<4 players, 1000<<4 others).
