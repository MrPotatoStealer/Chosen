# 20261023-181659: z-fighting texture

Status: fixed
Regression: room-over-room
Regression setup: send s30 on | 5
Regression check: look into the opened coffin from above: its rim edges must not flicker

## History

- 2026-10-23 18:16:59 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: The portal's copy of the room below baked the live sectors' original brush meshes (hidden in the level once the LiveRenderer draws them) and also mirrored the live mesh: the coffin's rim drawn twice in one place, seen only through the portal
- 2026-10-23 Fix: PortalRenderer copies skip meshes the level hides; zfight also scans the portal copies
