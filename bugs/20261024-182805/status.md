# 20261024-182805: pickup is seen from shared room

Status: fixed
Regression: stacked-rooms, sprites
Regression setup: nomonsters | 2
Regression check: the life seed in the room behind the wall does not show at the wall's corner

## History

- 2026-10-24 18:28:05 reported in maps/e2m2.map
- 2026-10-24 fixed. Cause: The life seed (and fire key) in sector 263 stand ~4 units behind a one-sided wall of sector 81, which shares space with 263 in the other layer; the sprite's edge poked through that wall. StackedRooms.SpriteHidden only hid a layer's sprite when it stood inside the other layer's footprint. Build never draws it: its walk from the camera stops at 81's wall.
- 2026-10-24 Fix: SpriteHidden also hides a sprite standing in one of the rooms sharing space when Build's walk toward it stops (one-sided wall) or ends in a shared room of the other layer (ShadeData.Walk overload returning the sector it stopped in; ShadeData.InLayered).
