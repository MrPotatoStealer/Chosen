# 20261016-100058: vase requires multiple hits to destroy with pitchfork, incorrect damage or hp?

Status: fixed
Regression: sprites, collision
Regression check: the vase breaks in one pitchfork hit

## History

- 2026-10-16 10:00:58 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Our hitscans tested face sprites against their clipdist box (the vase: clipdist 0 = a 1-unit pole), so most tines missed; Build's hitscan tests the sprite's picture (try_facespr_intersect).
- 2026-10-16 Fix: Hitscans (shots and use) test face sprites like try_facespr_intersect: within the sprite height and tilesizx*xrepeat/sqrt(128) of the centre. One swing breaks the vase.
