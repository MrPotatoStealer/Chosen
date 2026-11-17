# 20261027-092938: hole in geo

Status: fixed
Regression: geometry, movers

## History

- 2026-10-27 09:29:38 reported in maps/e1m4.map
- 2026-10-27 fixed. Cause: Region pieces of live room 397's pillar pockets (holes at ON) matched no group by overlap, corner or edge and fell to the first live group, which took them for its void: solid, with walls of its pictures (the spikes)
- 2026-10-27 Fix: A loose region piece goes with a region piece it borders, else the nearest group
