# 20261018-212040: should this hallway be this dark?

Status: fixed
Regression: sector-lighting

## History

- 2026-10-18 21:20:40 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Sector 177 is shade 63 (near black) with an always-on light wave of amplitude -46 (wave 2, freq 6): in Blood it pulses between shade 63 and 17. The light-effect skins were baked at the base shade and brightened through the entity colormod, but DarkPlaces sends colormod as colormod*32 in a byte (at most 7.97), and this needs about 37 times, so the hallway never got brighter than about shade 56. Sprites and the weapon had the same cap (a factor byte, 64 = 1, at most about 4).
- 2026-10-18 Fix: blood_sectorfx skins are baked at the brightest shade their effect reaches (convert_map fxLift = negative amplitude, added by bsp_shade), and the server only darkens them (factor <= 1). Sprites and the weapon now get the sector's shade offset (signed char) and the client adds it to the shade, like viewProcessSprites. E1M1 and E1M2 reconverted.
