# 20261019-190325: fog has collision

Status: fixed
Regression: masked-walls, collision
Regression check: walk through the fog

## History

- 2026-10-19 19:03:25 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: the fog is a masked wall without the blocking flags (cstat 144); the converter makes its hidden faces nodraw, but collision treated the masked face texture as solid
- 2026-10-19 Fix: blood_masked faces don't block; blocking masked walls (cstat 1/64) stay solid through their caulk faces
