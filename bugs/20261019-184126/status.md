# 20261019-184126: performance issues due to dynamic lights even when I cant see any?

Status: fixed
Regression: dynamic-lights, performance
Regression check: perf with dynamic lights and shadows on

## History

- 2026-10-19 18:41:26 reported in maps/e1m2.map
- 2026-10-19 fixed. Cause: every shadowed light redrew its cube shadow map (6 passes over the level) every frame: the light code set each light's range/shadow settings every frame and the voxel code set every model's transform, and spinning pickups cast shadows, each of which makes Godot redraw the shadow maps around it (55 fps vs 388 without shadows at the report's spot, 1920x1080)
- 2026-10-19 Fix: properties set only when they change, spinning voxels cast no shadow, moving lights use dual-paraboloid shadows: 384 fps with shadows at the same spot
