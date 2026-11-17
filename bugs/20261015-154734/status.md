# 20261015-154734: you can see distance objects in the sky, there needs to be some occlusion of the sky boxes as well to prevent this

Status: fixed

## History

- 2026-10-15 15:47:34 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Build's parallax sky hides everything behind it; the DarkPlaces 2014 cvar r_q3bsp_renderskydepth was registered but never used, so sky surfaces in Q3 maps did not write depth.
- 2026-10-15 Fix: Engine (tools/dpsrc2014 gl_rmain.c): sky depth masking enabled for Q3 maps when r_q3bsp_renderskydepth is set; default.cfg sets it.
