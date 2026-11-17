# 20261026-204454: some flashing here (changing sector drawing)

Status: fixed

## History

- 2026-10-26 20:44:54 reported in maps/e3m5.map
- 2026-10-26 fixed. Cause: The walk drawing rooms that share space started from the player's Build sector, but the eye is drawn at an interpolated, bobbed position that can be a few units over a wall into the next sector (274/275, which share space with 269): for a drawn frame the walk started in the wrong sector and faces swapped
- 2026-10-26 Fix: The view's sector is the eye's: from the player's sector across the walls to where the eye is drawn (World.SectorFrom, PlayerView.EyeSector)
