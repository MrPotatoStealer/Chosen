# 20261015-120044: texture is missing, I can see through the wall

Status: fixed
Regression: movers
Regression check: push the tomb: the pit side under it is textured once it lowers

## History

- 2026-10-15 12:00:44 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: The tomb (sector 0) is a floor that lowers 36 units when pushed. Side textures were chosen from each sector's starting heights, so the crypt floor's side facing the pit was caulk (invisible) and showed through once the tomb was lowered.
- 2026-10-15 Fix: convert_map.py: Converter.air() gives moving floors/ceilings their whole travel range; overlay_side_tex uses it.
