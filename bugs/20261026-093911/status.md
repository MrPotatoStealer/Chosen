# 20261026-093911: missing geo when the turning door is in motion

Status: fixed
Regression: movers, geometry
Regression setup: send 103 on | 3
Regression check: mid-turn: the ring's dark floor before the doorway, no lava

## History

- 2026-10-26 09:39:11 reported in maps/e3m5.map
- 2026-10-26 fixed. Cause: Ring 13's floor was drawn once from the converter's pieces, its drum's hole where Build stores it. The drum's loop dips in at the doorway, so as the cabin turned, the strip before the doorway had no floor and the void under it (lava) showed.
- 2026-10-26 Fix: Sectors whose corners a drawn mover carries (a turning ring's inner loop, the rooms beside sliding doors whose shared points Build drags) refill their floor and ceiling from the loops as they are now while moving (PolyFill: scanline fill, even-odd, like Build's).
