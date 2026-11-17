# 20261019-143701: is this shelf to bright?

Status: fixed
Regression: sector-lighting, movers

## History

- 2026-10-19 14:37:01 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the shelf is sector 70, a sliding (616) sector lit by its light effect (-42, on at start); Godot's light lookup tested its outline where Build stores it, not where it had slid, so its lamps and bottles got no light; its walls (a whole moving sector's own) took none; and its floor-standing bottles were moved twice (as moveWith sprites and as riders)
- 2026-10-19 Fix: light lookup follows a mover's offset; whole-sector movers light their side faces; moveWith sprites are not also carried as riders (NBlood TranslateSector moves a sprite once). Compared with NBlood (bugs.py nblood): same brightness
