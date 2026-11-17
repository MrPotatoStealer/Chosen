# 20261016-154112: you can stand on the small collision here, in NBlood it should not be possible and you fall to ground

Status: fixed
Regression: collision
Regression check: no standing on the alcove ledge behind the outdoor wall

## History

- 2026-10-16 15:41:12 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: The alcove behind the outdoor wall (sector 110, floor 17-37 units up) is closed by a blocking wall (cstat 1). GetZRange's probe box reached over the wall's clip slab and took the alcove floor as ground; Build's getzrange skips sectors behind blocking walls.
- 2026-10-16 Fix: GetZRange narrows its probe box until it no longer overlaps a blocking wall's clip slab (player/monster clip), so floors behind blocking walls are never ground.
