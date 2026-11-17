# 20261105-220910: shows incorrect sprite that is in a room over room. Zombie should not be visible in the mirror.

Status: fixed
Regression: stacked-rooms, sprites

## History

- 2026-11-05 22:09:10 reported in maps/test.map
- 2026-11-05 fixed. Cause: a mirror's copies were tested for rooms sharing ground with a walk starting at the eye reflected behind the mirror, outside its sector, where the mirror wall itself ended the walk unknown; and the test gave up first when the camera's own walk didn't see the sprite's sector (it looks the other way): the zombie lying in sector 16 (sharing ground with 0 beyond the mirror's room 18) was drawn
- 2026-11-05 Fix: the walk starts where the line from the reflected eye to the sprite crosses the mirror (SpriteRenderer.MirrorEntry, as Build walks in from mirrorsector) and a mirror's copy skips the camera visibility shortcut (StackedRooms.SpriteHidden mirror)
