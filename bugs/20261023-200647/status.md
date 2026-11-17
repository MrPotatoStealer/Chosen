# 20261023-200647: lift platform is not moving just the ceiling?

Status: fixed
Regression: movers
Regression setup: use | 4
Regression check: press use on the lift: floor and ceiling rise together

## History

- 2026-10-23 20:06:47 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: A z-motion sector moving both planes is two mover entities; the use key and wall pushes triggered only the part hit, so the ceiling rose alone
- 2026-10-23 Fix: TrTriggerSectorBoth: push and use trigger both parts (a decoupled sector's event once)
