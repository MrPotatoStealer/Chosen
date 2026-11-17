# 20261015-154952: gates are open from the start

Status: fixed
Regression: carried-sprites

## History

- 2026-10-15 15:49:52 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: The graveyard gates are sprites that the sliding sectors 65/90 carry (cstat 8192/16384); sprites never moved with sectors and were left at their stored (open) position.
- 2026-10-15 Fix: Converter places carried sprites at OFF and tags them (moveWith/moveSign); sectors with only moving sprites get an invisible driver mover; qc moverMoveSprites() moves/rotates them with the mover.
