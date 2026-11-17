# 20261015-154822: zombies should wake up from the graves when walking to this area

Status: fixed

## History

- 2026-10-15 15:48:22 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Buried zombies (type 205) fell through the world: their buried animation puts the origin inside the floor brush and GetZRange's trace started in solid and found no floor; line of sight from the sunk eye also failed (Build's cansee ignores heights inside a sector).
- 2026-10-15 Fix: engine.qc: GetZRange searches upward for open space when the origin is in solid; cansee moves endpoints in solid to the nearest open space; world.qc spriteRestOnFloor() lifts gravity sprites placed below the floor at level start. Tested: the zombie rises from the grave and attacks.
