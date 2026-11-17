# 20261015-163742: geomoetry is poking out from the wall

Status: fixed
Regression: room-over-room, geometry

## History

- 2026-10-15 16:37:42 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Room-over-room link 0 (sector 65 ceiling <-> sector 90 floor, offset 32,-32) was converted by dragging sector 65 under sector 90; every wall attached to it got stretched (skewed doorway, geometry poking out of walls).
- 2026-10-15 Fix: Room-over-room is now converted like Blood renders it: geometry stays at its Build position, each link side is a blood_link entity (botclip footprint slab). Tile-504 sides are portals drawn by the client through DarkPlaces camera surfaces (camera_transform = link offset, seamless, clipped at the plane). QC: Blood CheckLink (crossing the plane inside the footprint moves the sprite), GetZRange and hitscans follow links, ClipMove keeps the hull on its side of the plane; the view follows the eye across. Generic for all maps.
