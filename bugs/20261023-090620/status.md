# 20261023-090620: whole in geo after iceblock moves

Status: fixed
Regression: movers, geometry
Regression setup: send s0 on | 12
Regression check: after the floe and strips have moved: no hole between the underwater strip's sloped floor, its side wall and the water ceiling

## History

- 2026-10-23 09:06:20 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: Underwater strip 185's floor is aligned up to its ceiling (AlignSlopes sets both planes); its side walls' stretch sections took max(next ceiling, own floor) at each end and were drawn straight between, leaving a hole where the two cross.
- 2026-10-23 Fix: lid_stretch: where the two planes cross along the wall the section takes the next room's plane at both ends; the sector's own floor hides the rest.
