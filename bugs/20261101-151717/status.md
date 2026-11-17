# 20261101-151717: light shining through wall is back

Status: fixed
Regression: dynamic-lights

## History

- 2026-11-01 15:17:17 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: With r_skyfaces 0 the faces and windows the walk hides were also left out of the lights' shadow passes, so hidden walls stopped no light.
- 2026-11-01 Fix: WorldShader: culling only in the camera's own view (b3d_eye); shadow passes see the whole level. At the report the lighting now matches r_skyfaces 1.
