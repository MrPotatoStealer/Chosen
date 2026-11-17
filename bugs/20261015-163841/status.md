# 20261015-163841: this whole in the map is still not fixed, I can see the sky between the ground, for easier replication make sky purple or something while testing.

Status: fixed
Regression: geometry

## History

- 2026-10-15 16:38:41 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Step face between sloped sector 5 and sector 16 (wall 38) got caulk: the overlay picked side textures using the floor height at the cell centre, where the sloped floor is lower than the neighbour's floor, so no wall overlapped the face.
- 2026-10-15 Fix: Overlay side textures now use the face's height range along the edge itself. check_map also probes towards each face corner, so partly exposed faces are reported.
