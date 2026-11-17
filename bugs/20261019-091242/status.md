# 20261019-091242: voxel/sprites are not being rendered

Status: fixed
Regression: sprites

## History

- 2026-10-19 09:12:42 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: voxel models were degenerate meshes: the MD3 surface header was read 4 bytes off (numShaders taken as numVerts)
- 2026-10-19 Fix: Voxels.LoadMd3 reads numVerts/numTriangles/offsets at their right places
