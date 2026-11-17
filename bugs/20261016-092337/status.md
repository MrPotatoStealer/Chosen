# 20261016-092337: whole in geo, missing texture?

Status: fixed

## History

- 2026-10-16 09:23:37 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: Last round moved the light-effect skins just inside the solid (to close corner slits). DarkPlaces draws a brush entity only if its model box touches a visible leaf; a box wholly inside the wall touches none, so those rooms' walls and ceilings vanished. The new stretch-wall point entities also had no origin and leaked the map.
- 2026-10-16 Fix: Skins are back in front of the surface; at outside corners a skin's end face is textured so no slit opens. Stretch walls get an origin in open space, the converter warns about point entities without one, and build_map.bat warns when q3map2 leaks. Sprites now use DP's built-in null model so the server culls them by their physics box rather than their origin point (a cobweb on a ceiling was never sent).
