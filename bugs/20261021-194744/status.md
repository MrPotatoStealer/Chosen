# 20261021-194744: missing sides?

Status: fixed

## History

- 2026-10-21 19:47:44 reported in maps/e2m2.map
- 2026-10-21 fixed. Cause: E2M2's gate under the arch is a masked wall (tile 330) between sectors 253 and 254, whose ceilings slope from 256 down to 218 across it. The converter built masked (and one-way) walls as a box from the highest floor to the lowest ceiling point, so the gate was cut flat at 218 and the triangle up to the slope was missing; Build draws a mask up to the sloped ceiling at each column.
- 2026-10-21 Fix: Converter: opening_planes gives masked and one-way walls sloped bottom and top planes through each end's higher floor and lower ceiling. All E1/E2 maps reconverted.
