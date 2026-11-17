# 20261021-200826: looks strange between the areas and also having issues jumping out of the water

Status: fixed

## History

- 2026-10-21 20:08:26 reported in maps/e2m2.map
- 2026-10-21 fixed. Cause: Switch channel 104 raises E2M2's water by 28: the water room floors (150/151) and the pool ceilings (152/153) move, but the water links' planes were fixed at conversion, so after the switch the surface, the pool's water range and the links disagreed: the view came from the room above while the palette said underwater, and climbing out went through the wrong plane. Blood's CheckLink uses the sectors' planes as they are.
- 2026-10-21 Fix: Link sides on a z-motion sector's moving plane follow it (World.UpdateLinkPlanes after the busy processing: the plane, the footprint slab and the offset to the other side); with the moving water range of the pool (sOpen) the swim test, view and links agree. Also links shows which sides move.
