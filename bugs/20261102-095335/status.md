# 20261102-095335: look into performance of this level

Status: fixed
Regression: performance
Regression setup: perf | 3
Regression check: perf: about 4000 draw calls here, not 10000

## History

- 2026-11-02 09:53:35 reported in maps/dwe2m1.map
- 2026-11-02 fixed. Cause: Every dynamic light in the pool (24) cast shadows, most of them lamps 1400 to 4000 units off barely lighting the view, and turning fans and other moving sectors in their reach made each redraw all six faces of its shadow map every frame with everything near it, voxel models included: 10400 draw calls, 9.3 million primitives (9000 of the calls shadow passes).
- 2026-11-02 Fix: Only the r_shadowlights (8) most important lights cast shadows (brightness and reach over distance to the camera, the ones casting kept unless another is clearly more, a 0.35 s shadow fade on change), none past r_shadowdist (1500, Godot's distance fade). Here 3950 draw calls, render CPU 6.2 to 3.0 ms, GPU 7.3 to 4.1 ms (1280x720); the view looks the same. Menu: Enhanced > Lighting, SHADOW CASTERS and SHADOW DISTANCE. Regress dynamic-lights unchanged.
