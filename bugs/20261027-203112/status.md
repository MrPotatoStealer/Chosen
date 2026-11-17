# 20261027-203112: texture should be scrolling

Status: fixed
Regression: masked-walls
Regression check: the waterfall (masked wall 449/461) scrolls down

## History

- 2026-10-27 20:31:12 reported in maps/e4m3.map
- 2026-10-27 fixed. Cause: masked walls never scrolled: E4M3's waterfall 449/461 is a masked wall with panYVel
- 2026-10-27 Fix: a scrolling masked wall becomes a blood_sectorfx entity with the wall's pan keys (tile from overpicnum)
