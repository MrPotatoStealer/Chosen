# 20261117-113226: another spot with performance

Status: fixed
Regression: performance
Regression setup: "" | 3
Regression check: perf at the spot: about 3600 draw calls (was 8700); movingmesh shows 420 and 1059 shadow still

## History

- 2026-11-17 11:32:26 reported in maps/sevict.map
- 2026-11-17 fixed. Cause: Shadowed dynamic lights redrew their shadow maps every frame: Eviction's turning sector 420 (type 617) and the room round it (1059, rebuilt each drawn frame) are shadow casters whose meshes change every frame, so Godot redrew the cube maps of every shadowed light near them (the street's lamps, the two lights on the turning sector, the fire): about 4000 of the 8700 draw calls. Also every world chunk and voxel model's shader read TIME (the water caustics) and can discard, which Godot treats as an animated material: lights with any of them in reach redrew their maps every frame even with nothing moving.
- 2026-11-17 Fix: NativeWorld: a moving sector whose moves change no shadow (each wall that moved flush: two-sided, nothing drawn on it, the same level unsloped floor and ceiling either side; its planes still; its moving neighbours too) casts from a shadows-only copy, all such copies built together when that set changes, its drawn mesh casting none (r_stillshadows). WaterFx: the water's clock is a global uniform (water_time), not TIME. At the spot: 8670 to 3630 draw calls, render CPU 4.4 to 2.6 ms, 95 to 133 fps.
