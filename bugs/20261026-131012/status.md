# 20261026-131012: geo here looks very wrong

Status: fixed
Regression: movers, geometry
Regression setup: nomonsters | 0

## History

- 2026-10-26 13:10:12 reported in maps/e3m1.map
- 2026-10-26 fixed. Cause: The bar counter 41 and its back 248 (615, every corner turning about their shared corner) border rooms sharing space, where live sectors aren't handled: 41 took the inner-loop branch, its own outline a hole, and got no mover
- 2026-10-26 Fix: convert_map.plan_doors: a marked slide/turn whose every corner moves (moves_whole) beside rooms sharing space, or beside a room over one, is a 616/617 (TranslateSector moves it the same): a rigid whole mover
