# 20261023-181544: looks like the tracks following with the train should have a scrolling texture to make it be in sync with the ground around it at all times

Status: fixed

## History

- 2026-10-23 18:15:44 reported in maps/e1m2.map
- 2026-10-23 fixed. Cause: A mover's faces carry their texture with them; Build's floors and ceilings are world-aligned unless relative (stat 64), so the flat car's tracks (sector 10, floorstat 4) stay in step with the ground as it slides.
- 2026-10-23 Fix: The converter marks a whole sliding sector's world-aligned planes (worldFloor/worldCeil); MoverRenderer gives those faces their own materials and pans them by the travel along the face's texture axes (pan_uv).
