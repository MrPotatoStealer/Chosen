# 20261101-134617: need a sky fix, parts of houses are not visible

Status: fixed

## History

- 2026-11-01 13:46:17 reported in maps/e6m1.map
- 2026-11-01 fixed. Cause: Sky faces painted over the buildings behind them, as Build draws but with true perspective.
- 2026-11-01 Fix: r_skyfaces 0 is now the default: faces draw nothing, the background shows, the walk hides what it does not reach. The tall building and the skyline match NBlood at the report view.
