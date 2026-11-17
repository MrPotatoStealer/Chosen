# 20261019-122623: standing and jumping on grave puts camers out of bounds?

Status: fixed
Regression: collision
Regression check: jumping on the grave keeps the head under the sky

## History

- 2026-10-19 12:26:23 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the port treated a sky ceiling as no ceiling for everything: jumping from a grave put the player's head (and the camera) above the outdoor sky height, over the hedges. NBlood's MoveDude passes PARALLAXCLIP_CEILING|PARALLAXCLIP_FLOOR to GetZRange: a sky ceiling/floor stops players and monsters; only things and missiles pass
- 2026-10-19 Fix: Physics.GetZRange takes NBlood's clipParallax flag; MoveDude's three calls pass it (sky floors handled the same way)
