# 20261019-091501: sector is all dark

Status: fixed
Regression: sector-lighting

## History

- 2026-10-19 09:15:01 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the light-effect shade offset was a Godot instance uniform; slots of sprite nodes created and freed during play ended up on the level's meshes, which never set theirs again, so the world near the funeral home entrance read a shade of 60+ (black)
- 2026-10-19 Fix: fx_shade is a material uniform; each light-effect skin has its own copies of its materials
