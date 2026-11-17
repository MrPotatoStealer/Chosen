# 20261019-210314: duplication of the lift exist preventing access to top

Status: fixed
Regression: movers
Regression setup: send s136 on | 4
Regression check: with the lift up, its sides fill the doorway (no invisible face)

## History

- 2026-10-19 21:03:14 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: The lift (sector 136, z-motion floor and ceiling) had caulk on its floor part's sides: nothing is beside them at OFF, so with the lift up the doorway showed nothing (an invisible solid face).
- 2026-10-19 Fix: A z-motion part's side face with nothing beside it at OFF takes the wall beside its range at ON (the doorway's lower wall, tile 498), pegged to the OFF height.
