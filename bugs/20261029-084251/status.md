# 20261029-084251: missing boat?

Status: fixed

## History

- 2026-10-29 08:42:51 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: the shaders' stacked-rooms walk uses the walls as stored: the boat slid off its stored place, its inside faces met the room below the river there and were hidden
- 2026-10-29 Fix: faces of a whole mover away from its stored place, and the floor filled around its carried loop, skip the walk (LiveRenderer.Away)
