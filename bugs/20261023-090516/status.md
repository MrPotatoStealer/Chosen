# 20261023-090516: can fall in water before the sector is moved out of the way

Status: fixed

## History

- 2026-10-23 09:05:16 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The floe's floor skin over the shut water strip had no collision, and the strip's water link stayed live under it: as the shut water bobbed the link plane moved and a player standing on the floe dropped through into the water room.
- 2026-10-23 Fix: The skin is solid (it never stops or pushes anything), and a link covered by a planeWith skin is no link there (Link.Covered).
