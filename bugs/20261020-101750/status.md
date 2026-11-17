# 20261020-101750: still seeing performance issues this with dynamic lights off

Status: fixed
Regression: performance
Regression check: replay or play the station fight with r_perfoverlay 2: no tall bars, ticks under ~6 ms

## History

- 2026-10-20 10:17:50 reported in maps/e1m2.map
- 2026-10-20 fixed. Cause: Gibs and explosions made the game tick 12-17 ms (spikes 28): every collision trace tested all 78 brush entities and every body, floor/ceiling searches traced 8192 units through every brush in the column, SpriteSector searched all sectors for sprites in none; the sprite renderer called Godot for every sprite every frame (MaterialOverride getters, transforms, shader parameters; 2.4 ms a frame) and Godot arrays left to the finalizer contended with the main thread (stutter).
- 2026-10-20 Fix: Trace skips what its swept box misses (MayTouch), flat brush grid, 256-unit floor/ceiling search first (TraceZ), position-keyed sprite sector cache; sprite renderer sends only changes, face sprites turned and flat sprites lifted in the shader, cached light-effect sectors, portal visibility re-traced only when the eye moves, Godot arrays freed at once. Tick about 5.5 ms, sprites 0.6 ms, no finalizer stalls, replays identical. Added r_perfoverlay (F11) with a frame graph.
