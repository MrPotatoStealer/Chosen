# 20261029-105717: incorrect brightness on sprites in the ceiling?

Status: fixed
Regression: dynamic-lights, sprites

## History

- 2026-10-29 10:57:17 reported in maps/dwe3m1.map
- 2026-10-29 note: the brightness is our dynamic lights on the ceiling fixtures (r_dynamiclights 0 matches NBlood exactly); also the palette-1 shading no longer whitens by shade
- 2026-10-31 fixed. Cause: The ceiling tube (pic 796, a two-sided floor sprite) took no dynamic light from below: the sprite vertex shader turned its normal to the camera, and the engine flips a two-sided face's normal again when its back is seen, so it pointed away from every light on the seen side. The ceiling glowed and the tube stayed at its dark Build shade.
- 2026-10-31 Fix: Sprite shader: the normal is turned to the viewer in the fragment (dot(NORMAL, VIEW)). Any wall or floor sprite seen from its back is now lit.
