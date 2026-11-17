# 20261026-143245: fell of of world

Status: fixed
Regression: collision, movers
Regression setup: nomonsters; send 117 on | 6
Regression check: from room 141 (2134 -1137, floor -56) walk east under the raised crates: blocked, no fall

## History

- 2026-10-26 14:32:45 reported in maps/e3m2.map
- 2026-10-26 fixed. Cause: The crates 152 rise 460 units, their mover's solid only 112 thick (from the map's bottom): raised, the column under them was open and the room beside it (141, floor -56) walked in under them to the bottom
- 2026-10-26 Fix: overlay_world: a floor rising further than its solid is thick reaches down as far as it rises, and the column under where it starts stays solid (caulk: a brush of nodraw faces doesn't collide)
