# 20261026-125330: add missing halo to plasma gun projectiles

Status: fixed

## History

- 2026-10-26 12:53:30 reported in maps/e1m2.map
- 2026-10-26 fixed. Cause: viewProcessSprites' kViewEffectTesla was never drawn: NBlood adds a halo sprite (tile 2135, the bolt's repeat, translucent, shade -128) behind each regular tesla bolt
- 2026-10-26 Fix: SpriteRenderer.DrawTeslaHalo draws it in the bolt's flame node, centred on the bolt and 2 units further from the camera
