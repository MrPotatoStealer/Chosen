# 20261019-130320: game is overall to bright, or the contrast between light and dark areas is not very visible

Status: fixed
Regression: sector-lighting

## History

- 2026-10-19 13:03:20 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: Godot's 3D shaders work in linear light: multiplying a texture by Blood's shade factor there (x0.34 at shade 42) shows as about x0.6 on screen, so every shaded surface was too light and dark sectors lost their contrast. Blood (and its linear shade tables, checked in NORMAL.PLU) darkens the displayed colour
- 2026-10-19 Fix: the world, sprite and voxel shaders apply the shade factor in display space (factor^2.2 in linear); option b3d_shadecontrast (Options > Video > Sector Contrast, default 1 as Blood) scales shades for more contrast
