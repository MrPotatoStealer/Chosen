# 20261021-084032: some more geo issues here, room in room?

Status: fixed
Regression: stacked-rooms, room-over-room

## History

- 2026-10-21 08:40:32 reported in maps/e1m6.map
- 2026-10-21 fixed. Cause: Sector 198 lies wholly inside upper room 256's heights (a pocket): its ceiling has a light effect, and the converter's light skin of that ceiling (and the pocket's wall skins) used the untagged texture, so the stacked-rooms test never hid them: a grey strip of 198's ceiling showed at the far wall of 256 (through 257's ceiling portal too).
- 2026-10-21 Fix: convert_map.py: a pocket's ceiling skin and wall skins take view_tag like the faces they replace.
