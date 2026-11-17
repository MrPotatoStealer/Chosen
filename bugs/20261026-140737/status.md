# 20261026-140737: texture is upside down

Status: fixed
Regression: geometry, movers
Regression setup: nomonsters | 0

## History

- 2026-10-26 14:07:37 reported in maps/e3m2.map
- 2026-10-26 fixed. Cause: Build wraps a wall picture whose height isn't a power of two at the next one; the sector renderer wrapped it at the tile's height: the clock (tile 720, 96 high) pegged 128 above its section began 32 rows down, its face at the bottom
- 2026-10-26 Fix: LiveRenderer.NpotShift: each face starts on Build's row (mod the power of two) when that row is in the tile
