# 20261028-174211: missing water surface?

Status: fixed
Regression: room-over-room, sprites

## History

- 2026-10-28 17:42:11 reported in maps/mrw01.map
- 2026-10-28 fixed. Cause: The water surface is two translucent floor sprites (750/751, tile 2915) 4 units under link floor 341: the converter gave them a home range (hidden under their sector's floor) and the portal clipped them; Build draws a sector's sprites over its link floor
- 2026-10-28 Fix: Converter: no home range for floor sprites up to 16 units under a link floor (tile 504); SpriteRenderer.UnderOwnPortal: such a sprite of the link's own sector isn't clipped by the portal unless a portal copies it
