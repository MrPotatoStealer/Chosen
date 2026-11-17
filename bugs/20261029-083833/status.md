# 20261029-083833: tree is still cut off in sky

Status: fixed

## History

- 2026-10-29 08:38:33 reported in maps/dwe1m1.map
- 2026-10-29 fixed. Cause: the sky faces hid sprites rising more than r_skydepth past them
- 2026-10-29 Fix: r_skymode: on Build maps the sky draws after the level over what lies behind it, never over sprites and voxels (stencil); TrenchBroom maps keep solid faces
