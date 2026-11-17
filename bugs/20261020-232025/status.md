# 20261020-232025: very bad performance

Status: fixed
Regression: performance
Regression check: stand by the carousel while it turns: steady frame rate (game tick t.busy ~2 ms, was 20 ms)

## History

- 2026-10-20 23:20:25 reported in maps/e1m4.map
- 2026-10-21 fixed. Cause: E1M4 carousel (rotating mover): MoverPlace ran up to three full traces (world, all brush entities, all bodies) for every sprite near it, ~20 ms a game frame
- 2026-10-21 Fix: CollisionWorld.MayReport: the mover's own brushes are traced first and the full trace runs only if they are touched (same results); with the E1M6 fixes, over 12 ms frames 74 -> 4
