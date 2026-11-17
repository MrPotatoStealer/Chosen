# 20261020-231711: more performance issues

Status: fixed

## History

- 2026-10-20 23:17:11 reported in maps/e1m6.map
- 2026-10-21 fixed. Cause: E1M6 game tick ~10 ms every frame: QCanSee (AI target checks, autoaim) ran full nearest-hit traces gathering and sorting every brush along each line; C# built without JIT optimisations; every trace tested all 137 brush entities' bounds afresh
- 2026-10-21 Fix: CollisionWorld.LineBlocked: any-hit line test stopping at the first brush that settles it (same answer as the trace); Optimize on in the csproj; brush entity bounds cached until moved. Over 12 ms frames in the replay: 2856 -> 14
