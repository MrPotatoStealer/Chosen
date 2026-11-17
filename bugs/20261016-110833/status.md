# 20261016-110833: fullbright of the fire is gone again

Status: fixed

## History

- 2026-10-16 11:08:33 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: q3map2 had packed the fire face's lightmap into texels shared with a light-effect skin in another room (shade 42 won); unlit maps make every lightmap identical, so the packing shares texels.
- 2026-10-16 Fix: Converted maps are vertex-lit (q3map2 -nolm): bsp_shade writes each Build surface's shade into its vertex colours, splitting vertices shared by surfaces of different shades. The fire is at shade -122 again.
