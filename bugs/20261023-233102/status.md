# 20261023-233102: small hole/ missing geo when curtains are open

Status: fixed
Regression: movers, geometry

## History

- 2026-10-23 23:31:02 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: The curtain's slot lies outside sector 162 as built (its cloth is the slot's walls); the region pieces there neither overlap 162 nor share a corner with it, so they went to the first live group as void, and the opened slot showed a groove to the room below
- 2026-10-23 Fix: A region piece lying along a group's sector goes to that group (World.Live: Touches), before falling back to the first group
