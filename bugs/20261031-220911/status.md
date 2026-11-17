# 20261031-220911: train sky sector is making a strange cut in wall/ or renderign is strange here

Status: fixed
Regression: geometry
Regression setup: cam -2623.57 -1507.77 537.69 135 0 | 2
Regression check: the top of the cutting wall runs straight past the train

## History

- 2026-10-31 22:09:11 reported in maps/e1m2.map
- 2026-10-31 fixed. Cause: Sky faces paint over all behind them. E1M2's train sector stands wholly in the cutting's sky sector with a lower sky ceiling, and that plane cut the cutting's far wall along the train's outline. Build paints no sky at an opening between two skies, so the wall shows to its full height.
- 2026-10-31 Fix: WorldMesher.SkyOver: a sky sector with only sky neighbours has its sky at the highest neighbour's height and no band round it. Matches NBlood at the report's camera.
