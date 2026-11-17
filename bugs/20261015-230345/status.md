# 20261015-230345: blowing a whole in the wall here makes the floor go up?

Status: fixed
Regression: movers, carried-sprites
Regression check: blow the wall: the floor stays, only the fire sprites rise

## History

- 2026-10-15 23:03:45 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: sector 59 is a z-motion sprite sector (602): Blood only moves the sprites flagged to follow it (VSpriteBusy), but it was converted as a moving floor, so blowing the wall raised the whole floor
- 2026-10-15 Fix: 602 sectors are static at their level-start height; their cstat 8192/16384 sprites move with VSpriteBusy
