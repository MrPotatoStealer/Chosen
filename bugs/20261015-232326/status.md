# 20261015-232326: fire should not be visible before blowing up the funeral home

Status: fixed
Regression: stacked-rooms, carried-sprites

## History

- 2026-10-15 23:23:26 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the fire sprite belongs to sector 59 upstairs and waits 88 units under its floor until the TNT is blown; Build only draws a sprite inside its own sector, in 3D that spot is open air in front of the funeral home
- 2026-10-15 Fix: converter gives sprites that start outside their sector's open range homeLo/homeHi; the server hides them while wholly outside it
