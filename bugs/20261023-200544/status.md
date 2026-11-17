# 20261023-200544: whole in geo

Status: fixed
Regression: stacked-rooms, movers
Regression check: the train car's wall tops run straight, no beam or stone poking through

## History

- 2026-10-23 20:05:44 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: Same as 20261023-200532: the sliding door's panel wall (beam on the left, grey stone on the right) drawn into the train car below
- 2026-10-23 Fix: Stacked live sectors' materials registered with StackedRooms
- 2026-10-23 note: Hairline seams in the car's wall-top trim: the band strips were clipped along a line half a unit off the wall, ending up to half a unit short where the corridor's outline crosses the wall slanted; now measured over the half-unit band, so they reach the wall's corners
