# 20261026-143500: incorrect geo

Status: fixed
Regression: stacked-rooms, movers
Regression setup: nomonsters | 0

## History

- 2026-10-26 14:35:00 reported in maps/e3m2.map
- 2026-10-26 fixed. Cause: With the crates 135 ordered over the tunnel 169 (141026's fix), the overlap at OFF (crates at -56, below the tunnel's ceiling) was no stacked pair: from the pit the tunnel's faces showed
- 2026-10-26 Fix: overlay_world: a moving floor below the lower room's ceiling is a stacked pair too, each hidden from the other's side
