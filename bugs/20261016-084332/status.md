# 20261016-084332: incorrect light on secret that can be blasted open, I guess because of light changes with lightswitch

Status: fixed
Regression: sector-lighting

## History

- 2026-10-16 08:43:32 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: The breakable panel is a sprite in a light-switch sector; sprites kept the baked lightmap shade while Blood adds the sector's current floor shade (viewProcessSprites).
- 2026-10-16 Fix: Sprites get their sector's light-effect brightness from the server (floor, or ceiling under a sky); the weapon takes the player's floor effect.
