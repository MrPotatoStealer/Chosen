# 20261105-153923: jumped and got stuck in the air

Status: fixed

## History

- 2026-11-05 15:39:23 reported in maps/test.map
- 2026-11-05 fixed. Cause: Not the game: Forge's Add Floor above sector 11 with the Goo link (remembered from the dialog's last use) made sector 11, the lower room, underwater; jumping in from sector 10 across a wall entered the water sideways, and underwater there is no gravity (NBlood the same)
- 2026-11-05 Fix: Forge: the Add Floor dialog remembers nothing (each time Above, plain); water or goo under a room underwater already linked with stack markers and 504, no surface (E4M4's way)
