# 20261028-174551: some moving object like this door causing some meshes to have holes for 1 frame when moving

Status: fixed

## History

- 2026-10-28 17:45:51 reported in maps/mrw01.map
- 2026-10-28 note: Likely the blend between game frames pairing vertices of different faces (same counts after the pieces are cut anew; E2M6's ice floe 464 units, E1M8/E1M6 too): LiveRenderer.Pairs now refuses such pairings. User to recheck at mrw01-door (timescale 0.1).
- 2026-10-28 fixed. Cause: Moving meshes blended between game frames paired vertices of different faces: same materials and vertex counts after the pieces were cut anew (E2M6's ice floe up to 600 units apart, E1M8, E1M6)
- 2026-10-28 Fix: LiveRenderer.Pairs: a pairing is refused when a vertex moves further than the movers could carry it (doubled plus 8 units); that frame is drawn unblended. timescale and r_lerpcheck added for checking. User confirmed it looks much better
