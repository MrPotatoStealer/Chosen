# 20261031-220422: can we fix so lights behind wallks are not shining through. Double sided geo on shadows maybe?

Status: fixed
Regression: dynamic-lights
Regression check: with shadows on: no torch light through the wall from the next room

## History

- 2026-10-31 22:04:22 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: World walls only face their own room and cast shadows the same way, so a wall seen from behind stopped no light: a light in the next room lit this one through it. Unshadowed link copies also glowed through walls in the volumetric fog, which ignores their region. About 5% of the sprites' lights also sat past their wall or ceiling and only reached their room through that same hole.
- 2026-10-31 Fix: World and moving meshes cast shadows from both sides; a link's copy gives no fog glow; DynamicLights.KeepInRoom keeps a sprite's light inside the sprite's sector.
