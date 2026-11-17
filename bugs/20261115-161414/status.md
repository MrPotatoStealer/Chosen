# 20261115-161414: black room over room. Rendering not working correctly here

Status: fixed
Regression: room-over-room, stacked-rooms
Regression setup: cam 1569.45 -1943.50 261.94 328.89 17.79 | 2
Regression check: the street below shows through the balcony's floor

## History

- 2026-11-15 16:14:14 reported in maps/sevict.map
- 2026-11-15 fixed. Cause: The balcony's floor link (sector 19 to sector 1, offset 32 32 20) shows the room below, which shares its place with the balcony: the copy held the balcony's own floor face 20 units under the plane, black. The stacked rooms' walk that drops it started in the far sector from the moved eye, which stands outside it (over the next sector of the lower room), so it left by the wall the line came in through and never arrived in the far sector.
- 2026-11-15 Fix: The walk of a link's copy starts where the eye's line comes through the link's plane, in the sector a walk along the plane from a place in the far sector arrives in (StackedRooms.Shader stack_discard, portal_data's FrameColumn for floor and ceiling links; SpriteHidden the same for the copies' sprites: ViaPlane, ViaStart).
