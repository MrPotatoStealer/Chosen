# 20261020-231817: very bad performance

Status: fixed
Regression: performance
Regression check: play through E1M6 with monsters: no stutter (game tick was ~10 ms every frame, AI line-of-sight checks)

## History

- 2026-10-20 23:18:17 reported in maps/e1m6.map
- 2026-10-21 fixed. Cause: E1M6 game tick ~10 ms every frame: QCanSee (AI target checks, autoaim) ran full nearest-hit traces gathering and sorting every brush along each line; C# built without JIT optimisations; every trace tested all 137 brush entities' bounds afresh
- 2026-10-21 Fix: CollisionWorld.LineBlocked: any-hit line test stopping at the first brush that settles it (same answer as the trace); Optimize on in the csproj; brush entity bounds cached until moved. Over 12 ms frames in the replay: 2856 -> 14
