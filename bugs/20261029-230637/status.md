# 20261029-230637: look at performance here

Status: fixed
Regression: performance

## History

- 2026-10-29 23:06:37 reported in maps/dwe3m1.map
- 2026-10-31 fixed. Cause: Measured at the report's spot at 3440x1440: the sprite pass held 1.8 ms of dynamic lights (each map light searching for its sector every frame), moving sectors were built again each drawn frame, and water portals drew every mesh near them per surface.
- 2026-10-31 Fix: Far lights rejected on distance, their sectors kept; moving sectors built once a game frame and blended in the shader; water copies only of the joined room. Standing at the spot 120 to about 175 fps, CPU 4.1 to 1.5 ms; the DWE3M1 replay p99 12.7 to 7.5 ms (README Performance notes).
