# 20261026-182240: this looks incorrect

Status: fixed
Regression: geometry, stacked-rooms

## History

- 2026-10-26 18:22:40 reported in maps/e3m5.map
- 2026-10-26 fixed. Cause: Wall 264 of sector 22 leads into 35, whose wall there leads to 41 (a one-way portal in Build's data): the sectors drew each two-sided wall only from the sector beyond, so the face Build draws from 22 was never made; and the camera's sector was guessed by the renderer
- 2026-10-26 Fix: The converter writes rlower/rupper for such walls and the sectors draw them from this side (LiveRenderer); the camera's sector is the player's tracked Build sector (World.SectorTrack)
