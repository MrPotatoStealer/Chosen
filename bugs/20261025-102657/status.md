# 20261025-102657: dynamic lights in room in room?

Status: fixed
Regression: dynamic-lights, stacked-rooms

## History

- 2026-10-25 10:26:57 reported in maps/e2m5.map
- 2026-10-25 fixed. Cause: Sprite 121 (a torch) is in sector 136, a closed-off niche whose floor (144) lies below the ceiling (160) of corridor 694 and room 691 under it; its dynamic light (z 152) is inside their space. The sprite was hidden from them by the stacked-rooms walk, but its light was not.
- 2026-10-25 Fix: DynamicLights.Add: a sprite's light is left out wherever StackedRooms.SpriteHidden hides the sprite from the camera (torches, fixtures, explosions, projectiles, burning).
