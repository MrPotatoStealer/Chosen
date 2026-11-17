# 20261019-130147: there should be an increase in brightness on the ground when the lid opens

Status: fixed
Regression: sector-lighting, movers, room-over-room
Regression setup: send s30 on | 4
Regression check: the stairwell floor brightens as the coffin lid opens

## History

- 2026-10-19 13:01:47 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the stairwell (sector 30) has a light effect that brightens as the coffin lid slides open; the port, like the DarkPlaces version, skipped light effects of moving sectors. NBlood InitSectorFX lights every x-sector with an amplitude
- 2026-10-19 Fix: moving sectors take part in the light pass; their moving surfaces get the sector's shade (MoverRenderer) and the converter now gives their static walls and ceiling light-effect skins (maps regenerated)
