# 20261018-213410: voxel version of the fence/gate is upside down

Status: fixed
Regression: sprites

## History

- 2026-10-18 21:34:10 reported in maps/E1M1.bsp
- 2026-10-18 fixed. Cause: Refreshed Supply's extra voxels ignored the sprite's flips. The E1M1 gates (tile 1044) are wall sprites with cstat x-flip and y-flip, so the sprite shows the spikes up but the voxel was drawn unflipped, spikes down.
- 2026-10-18 Fix: Extra voxels flip with their sprite like Build flips a tile's voxel: y-flip mirrors the model upside down within the sprite's height (voxels.txt now carries each voxel's height), x-flip mirrors it across the pivot; a model mirrored on one axis is drawn double-sided (qc/client/voxels.qc drawVoxel). Blood's own view 6/7 voxels stay unflipped, as Blood clears their flips.
