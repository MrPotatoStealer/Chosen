# 20261027-092415: collision on moving platform here stopped working

Status: fixed
Regression: collision, movers
Regression check: jump east from the ledge onto the sliding platform (e3m5-platform): you land on it, not through the shaft's link

## History

- 2026-10-27 09:24:15 reported in maps/e3m5.map
- 2026-10-27 fixed. Cause: Same as 20261027-093224: shaft 211 was given sliding platform 212's area, the player stayed in 211 over the platform and fell through 211's floor link
- 2026-10-27 Fix: UpdateSector prefers the whole moving sector (the replay now lands on platform 212)
