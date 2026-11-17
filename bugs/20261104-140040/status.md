# 20261104-140040: floor texture moving around (jitter) when sector is moving

Status: fixed

## History

- 2026-11-04 14:00:40 reported in maps/test.map
- 2026-11-04 fixed. Cause: Sector 8 (Rotate) aligns its floor to its first wall, the 400-unit end of a 400 x 3376 block; Blood turns the walls' points and rounds them to whole units each frame, so the short first wall tips the picture by up to 1/400 rad: up to 0.58 texels (9 units) at the far end (NBlood draws relative floors from the same rounded points, so by its code it should wobble the same; not checked in NBlood)
- 2026-11-04 Fix: The map's: a long first wall (Shift+F on a 3376 side: under 0.06 texels). Forge's What it needs and Map Analysis now advise it for turning and sliding sectors with a relative floor or ceiling, Fix making the longest wall first
