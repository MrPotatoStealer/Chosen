# 20261030-155526: ground mist is confused here about the portal I think

Status: fixed

## History

- 2026-10-30 15:55:26 reported in maps/e1m1.map
- 2026-10-30 fixed. Cause: The mist areas joined sectors at any height (E1M1's cemetery front: the roofs and the yard), and the shader blended the cells' heights, a slanted sheet up the wall; and fog volumes take no portal clip, so the mist really behind E1M1's rooftop link lay over the portal's view
- 2026-10-30 Fix: Mist areas only across steps of 32 at most (a raised group over all its open surroundings has none); the shader blends the four nearest cells' layers, not their heights; and clips where an active portal shows another room (camera_world global)
