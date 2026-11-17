# 20261019-104148: z-fighting doors. looks like the the duplicate open doors remain in the map. Same issue we had in darkplaces a while back.

Status: fixed
Regression: movers

## History

- 2026-10-19 10:41:48 reported in maps/e1m1.map
- 2026-10-19 note: Godot had no depth priority between brush entities and coplanar world faces (DarkPlaces biases submodels, r_polygonoffset_submodel_offset): opened sliding-door halves flicker where they lie flush with the wall. Brush entities now draw a hair behind (world wins). Open question: are the ornate door panels inside the crypt (in both DarkPlaces and Godot) copies of the open doors left in the map by the converter? To check against NBlood.
- 2026-10-19 fixed. Cause: the converter kept a solid copy of a sliding panel (type 614/615 marked slide) at its stored ON position: the part sticking out of the wall pocket stayed in the static world under the door's mover, coplanar with it
- 2026-10-19 Fix: convert_map.py overlay_world: the area a panel covers at both ends of its travel is open space of the door's sector (the mover is the only solid there); E1M1 and E1M2 regenerated (21 and 18 static brushes fewer). Godot also draws brush entities a hair behind coplanar world faces
