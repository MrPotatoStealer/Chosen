# 20261030-134538: small edge in water

Status: fixed

## History

- 2026-10-30 13:45:38 reported in mods/dw/maps/dwe3m1.map
- 2026-10-30 fixed. Cause: SeamSkirt: the sector renderer's walls along a sector filled from its loops (DWE3M1's river 754, its boat's hole carried) reach 0.25 units past the next floor to close the fill's seams; between two river sectors at one height that is the whole wall, just under the water, a line through see-through water
- 2026-10-30 Fix: A seam skirt where Build has no wall, under a water flat, gets its own material, hidden (clip box) while the water is see-through
