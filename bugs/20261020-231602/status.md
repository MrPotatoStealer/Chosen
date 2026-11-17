# 20261020-231602: something is not correct with this geo, room in room issue?

Status: fixed
Regression: room-over-room, stacked-rooms

## History

- 2026-10-20 23:16:02 reported in maps/e1m6.map
- 2026-10-21 fixed. Cause: Upper room (sector 256) of the shaft's ceiling link is stacked with sectors 198/199; in a portal copy stack_discard and SpriteHidden skipped Build's walk and used the real camera's sector (the shaft), so 198/199's wall and torch showed through the ceiling
- 2026-10-21 Fix: Portal copies walk from the sector beyond the link (found at load, stored in portal_data's last column with the offset), the eye moved by the link offset, for faces (shader) and sprites (CPU)
