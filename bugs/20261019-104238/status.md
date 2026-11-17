# 20261019-104238: framerate issue/stuttering when shooting and looking. Check recording.

Status: fixed
Regression: performance
Regression check: shooting and looking around: no stutter (perf)

## History

- 2026-10-19 10:42:38 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: BrushModel.Trace visited every grid cell in the bounding box of a trace: a 16384-unit hitscan made tens of thousands of lookups per brush entity (up to 97 ms a game frame while shooting); also per-frame garbage from StringName conversions in the renderers caused periodic GC pauses
- 2026-10-19 Fix: traces are clipped to each model's bounds and walk the cells along the line (same brush order, identical results); shader parameter names are cached; console log updates are batched; the level's sprite tiles and voxels load with the level
