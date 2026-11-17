# 20261024-223856: strange sprite, might be a room in room problem here?

Status: fixed
Regression: sprites, stacked-rooms

## History

- 2026-10-24 22:38:56 reported in maps/e1m5.map
- 2026-10-24 fixed. Cause: The candle (sprite 74, sector 132, under the barrel room 203 and stacked with it) hangs exactly on 203's rack wall line; SpriteHidden only considered a sprite inside the other room's outline, and a point on the line failed that test, so the walk check that hides it never ran.
- 2026-10-24 Fix: StackedRooms.SpriteHidden: a sprite within 8 units of the stacked room's outline counts as in it (ShadeData.NearSector); the walk then hides it as Build does.
