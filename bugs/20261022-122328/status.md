# 20261022-122328: here is where I saw the AO in the sky

Status: fixed

## History

- 2026-10-22 12:23:28 reported in maps/e1m1.map
- 2026-10-22 fixed. Cause: The sky is level geometry here (Build's parallax ceilings are sky faces), so it has depth and the AO pass darkened it like any surface
- 2026-10-22 Fix: The sky material writes roughness 0 (the level and sprites are 1); the AO pass reads Godot's normal-roughness buffer and leaves those pixels alone
