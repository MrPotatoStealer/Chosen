# 20261018-220309: door on this side should also follow the pulasating light

Status: fixed
Regression: sector-lighting, movers

## History

- 2026-10-18 22:03:09 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: The closed manager door (z-motion sector 226) shows the doorway's wall 1617 (sector 227, which pulses with the hallway: amplitude -48), but the face belonged to the door's moving brush entity, and light-effect skins were only made for static faces, so it kept its baked shade 63.
- 2026-10-18 Fix: A z-motion door's face that shows a light-effect sector's wall now gets a skin in a blood_sectorfx entity keyed to the door (fxMoveWith = door name, moveOn = its part); the door's own face there is hidden (blood_fxhide). The server moves the skin with the door every frame after the doors move and pulses it with that sector (convert_map mover_fx_entities, sectorfx.qc). E1M2 has 14 such door faces.
