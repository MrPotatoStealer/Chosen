# 20261021-204555: when waterlevel is increasd this sector needs to go with it when inside water. looks fine from above the water

Status: fixed

## History

- 2026-10-21 20:45:55 reported in maps/e2m2.map
- 2026-10-21 fixed. Cause: E2M2's big pool ceiling (sector 153) stayed down while the small one (152) rose with the water: the player floats with the head through the ceiling's link plane, so the port's mover check found the player inside the rising ceiling and stopped it as blocked until the player swam away. Blood's VDoorBusy only stops a z-motion plane that closes the sector (a ceiling coming down, a floor going up).
- 2026-10-21 Fix: DoorBusy: a z-motion part (BUSYID_2 with a zPart) moving away from the sector's open space is never blocked, as VDoorBusy.
