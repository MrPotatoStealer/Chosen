# 20261019-182027: not sure where this dynamic ligt is coming from

Status: fixed
Regression: sector-lighting, dynamic-lights

## History

- 2026-10-19 18:20:27 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: not a dynamic light: the brighter strip (coffin, back wall, ceiling) is that sector's Blood light effect; NBlood shows it the same (compare.jpg). No dynamic light reaches the spot
- 2026-10-19 Fix: no change
