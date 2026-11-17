# 20261025-160648: teleport not working?

Status: fixed
Regression: collision
Regression setup: warp 545 32 -96 0 0; walk 40 | 3
Regression check: walking onto the teleporter takes the player to the secret room (invulnerability)

## History

- 2026-10-25 16:06:48 reported in maps/e3m5.map
- 2026-10-25 fixed. Cause: The converter never linked a teleporter sector (type 604) to its destination marker, so OperateTeleport had no destination and did nothing (all eight maps with teleporters). The alcove going black is Blood's own pulse (wave 5, shade 55 at its darkest), as NBlood shows.
- 2026-10-25 Fix: Teleport sectors get target marker<n> and warp markers (type 8) targetname marker<n>.
