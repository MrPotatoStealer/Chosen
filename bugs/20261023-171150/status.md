# 20261023-171150: collicion is really wonky around the tapering edges when getting out of the water

Status: fixed

## History

- 2026-10-23 17:11:50 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: GetZRange's box trace took a sloped floor's highest point within the box (the strip's floor sloping up beside the underwater room: 25.6 where Build's getzrange evaluates each sector's plane at the sprite's x, y: 17); the swimmer was lifted 25 units and the ceiling pushed back each other frame.
- 2026-10-23 Fix: A sloped floor hit is that plane at the centre (or what lies straight under it when higher), as Build's getzrange (Physics.AtCentre); ceilings keep the box's lowest point. The space there (39 units under a 56 ceiling for a 55-unit swimmer) still lifts and pushes back, by Blood's numbers (16).
