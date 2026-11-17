# 20261030-191513: performance is terrible while underwater

Status: fixed

## History

- 2026-10-30 19:15:13 reported in maps/e2m1.map
- 2026-10-30 note: Underwater, a frame costs about 65 ms: the bobbing lake's 119 live tiles are rebuilt every game frame (about 14 ms), and the water portal seen from below (the whole lake surface) copies 132 of them every frame (about 43 ms).
- 2026-10-30 fixed. Cause: Native water portals rebuilt every portal's face mask (64x64 cells, 9 samples against every triangle) and re-set the portal globals each game frame, as E2M1's ice floes (water links) slide and its water bobs.
- 2026-10-30 Fix: A plane-only change moves the face; masks are rebuilt only for reshaped portals, rasterised; data textures sent whole and globals set once: E2M1 10 -> 155 fps.
