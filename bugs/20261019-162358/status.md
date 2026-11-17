# 20261019-162358: small geo bug is still here

Status: fixed
Regression: geometry

## History

- 2026-10-19 16:23:58 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: a hairline across the sky (also the line in 143458): each brush's faces are clipped in float32 by func_godot, so two faces sharing an edge got corners differing in the last bits and the seam leaked single pixels of the void
- 2026-10-19 Fix: the converter writes _vertex_merge_distance (1/16 Quake unit, its point grid) on every brush entity, so func_godot snaps the generated vertices and shared corners are identical; E1M1 and E1M2 regenerated
