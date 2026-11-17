# 20261029-171922: fog is thicker or falloff is much lowe than Nblood

Status: fixed
Regression: sector-lighting

## History

- 2026-10-29 17:19:22 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: palette-1 faces were darkened and fogged linearly to white by distance, but NBlood (indexed textures) draws them through SATURATE by shade plus distance with no fog: near streets washed out, the distance too clear; shading also went by the distance to the eye, not depth
- 2026-10-29 Fix: SATURATE as a blend toward white by shade plus twice the distance shade (fitted to the table, measured on DWE3M1's comparison views), by the face's own palette; distance shading by view depth
