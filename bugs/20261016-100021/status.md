# 20261016-100021: sprites are not animated, also this fire sprite should be brighter

Status: fixed
Regression: sprites

## History

- 2026-10-16 10:00:21 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Tile 2342 is Blood's procedural fire (fire.cpp regenerates it while visible); we exported the static tile.
- 2026-10-16 Fix: pipeline/fire.py runs Blood's cellular fire with RFIRE.CLU and writes 64 frames played by animMap at 40 fps (walls, one-way walls, sprites).
