# 20261101-170716: look into if there is a fix for the bleeding of moonlight?

Status: fixed
Regression: dynamic-lights
Regression setup: r_moonlight 1 | 3
Regression check: no blue edges along the walls under the ceiling

## History

- 2026-11-01 17:07:16 reported in maps/e1m5.map
- 2026-11-01 fixed. Cause: The light from the sky used Godot's default shadow biases, which lift a surface just under a thin Build ceiling or corner above it, lit: blue edges in E1M5's rooms. Sector 291 has no opening within two sectors of the sky.
- 2026-11-01 Fix: Atmosphere: ShadowBias .02, ShadowNormalBias .3, ShadowBlur .25. World.NativeSkyFlags bit 4: more than two openings from the sky, no light from it (light_sky in BuildShading.LightFunction). Gone at the report; outdoor views with r_moonlight 2 look the same.
