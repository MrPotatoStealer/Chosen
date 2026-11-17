# 20261019-190046: duplicate doors and missing?

Status: fixed
Regression: movers

## History

- 2026-10-19 19:00:46 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: the double door's halves (sector 71, 614) each have three corners on the door sector and the fourth (with the front face) on the sector beside it; the converter built each panel from one sector's walls only, so it was a triangle whose open side faced the corridor (invisible), only its edges showed
- 2026-10-19 Fix: converter: a panel's chain is completed with the neighbouring sector's wall carrying the same marker (extend_panel)
