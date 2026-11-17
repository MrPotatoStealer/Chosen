# 20261026-141026: invisible collision after raising floor, can't progress

Status: fixed
Regression: collision, movers
Regression setup: nomonsters; send 117 on | 5
Regression check: walk up the raised crates from the report spot (south)

## History

- 2026-10-26 14:10:26 reported in maps/e3m2.map
- 2026-10-26 fixed. Cause: The crate column 135 rises from -56 to 172 right through the tunnel 169 (12-108) under it; overlay_world ordered the cell's rooms by their OFF floors, put the crates under the tunnel and made the column over the tunnel solid: the raised crates stood in it
- 2026-10-26 Fix: overlay_world: a floor rising right through another room in the cell sorts by its ON height (over that room)
