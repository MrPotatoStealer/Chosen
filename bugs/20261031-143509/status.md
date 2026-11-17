# 20261031-143509: standing on edge of boat, player does not move with boat

Status: fixed

## History

- 2026-10-31 14:35:09 reported in maps/e2m1.map
- 2026-10-31 fixed. Cause: No bug: as NBlood. The player's centre is over sector 125, the lake, with no Drag; only the hull sectors (87, 91, 92, 93) drag what stands on them. TranslateSector moves only sprites of the sector dragged, in NBlood too, and the player on the rim is in 125 by the clip distance's reach. Compared in the source, not played side by side.
- 2026-10-31 Fix: None.
