# 20261023-224829: geo has holes

Status: fixed
Regression: stacked-rooms, geometry

## History

- 2026-10-23 22:48:29 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: The converter skipped the stacked-room wall strips on every wall of a live sector (added for 210146), but room 31 is live only where the sliding car 159 touches it; the rest of it, its walls under corridors 155/160 included, is static and needs the strips in the shared band (z 32..56)
- 2026-10-23 Fix: Strips on a live sector's wall are left out only where the wall runs inside the live region (where the game draws it); elsewhere they are kept
