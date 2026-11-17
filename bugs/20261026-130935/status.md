# 20261026-130935: I can see the wrong room here, not sure if the collision is correct

Status: fixed
Regression: room-over-room, stacked-rooms
Regression setup: nomonsters | 0

## History

- 2026-10-26 13:09:35 reported in maps/e3m1.map
- 2026-10-26 fixed. Cause: Where rooms share space (E3M1's shaft 87, layer 1, over the bar 381), portals ignored layers: shaft 87's floor portal, its face black and its copy showing the pit 85 under it, was drawn from the bar
- 2026-10-26 Fix: PortalRenderer: a portal whose face is tagged with a layer (_g<n>) is neither drawn nor black where Build's walk from the camera reaches its place in the other layer (StackedRooms.LayerHidden)
