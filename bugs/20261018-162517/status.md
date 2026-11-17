# 20261018-162517: its should be dark inside this train cart?

Status: fixed

## History

- 2026-10-18 16:25:17 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: q3map2 stores a brush entity with an origin brush (rotating movers: the turntable car, swing doors) relative to its origin; bsp_shade looked its faces up at those relative coordinates, so the car took the shade of whatever Build surfaces lie near the map origin (or the default). The light grid also counted the car's interior as the outdoor room (whole-mover rings meant for doors that leave their spot).
- 2026-10-18 Fix: bsp_shade adds each model's entity origin before looking up its Build surface; step-rotating sectors no longer add their outline to the surrounding room in the shade data. Verified: the car's walls, floor and ceiling are dark (shades 39-63) and its sprites at Blood's 31.
