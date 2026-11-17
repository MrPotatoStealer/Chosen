# 20261019-091349: sprites should be double sided, but can only be seen from one side. Or transparent wall that is here?

Status: fixed
Regression: masked-walls

## History

- 2026-10-19 09:13:49 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the fence is a masked wall: one face of a thin brush, which DarkPlaces draws from both sides (blood_masked shaders: cull none); Godot's world shader culled back faces
- 2026-10-19 Fix: blood_masked surfaces get a two-sided copy of the world shader
