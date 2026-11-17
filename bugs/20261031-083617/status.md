# 20261031-083617: flying through portals no longer works

Status: fixed

## History

- 2026-10-31 08:36:17 reported in maps/e1m8.map
- 2026-10-31 fixed. Cause: The fly camera crossing a link kept the renderer's camera sector in the room it left (followed in plan: link 20 drops into 249 right under 243, 32 units across).
- 2026-10-31 Fix: FlyCamera forces StackedRooms' sector to the link's far room (SectorFrom its far sector).
