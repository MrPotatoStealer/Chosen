# 20261018-154100: got stuck in collision from firing and being pushed into the wall

Status: fixed
Regression: collision, movers
Regression check: pushed into the turntable car wall: no getting stuck

## History

- 2026-10-18 15:41:00 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Walking into the turntable car's inner wall: GetZRange leaves out any non-world entity its probe box starts inside (a statue or a door beside the actor), and the car's wall and floor are one mover entity, so the floor went too and the player dropped through the car floor to the ground under it, inside the car body.
- 2026-10-18 Fix: GetZRange narrows the probe box against a mover until it clears; only a mover the origin itself is inside is left out. Verified: walking into the wall keeps the player on the car floor (z 346).
