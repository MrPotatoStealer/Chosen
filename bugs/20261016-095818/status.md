# 20261016-095818: small tiney gaps all over the level, build might have truncated the vertices to make them align perfectly?

Status: fixed
Regression: geometry

## History

- 2026-10-16 09:58:18 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Light-effect floor skins sat 0.05 above the floor, leaving a notch at step edges where the unseen world face shows the void.
- 2026-10-16 Fix: Skins lie exactly on their surfaces again (slab behind); each blood_sectorfx entity gets an anchor brush in its room so DarkPlaces doesn't cull it.
