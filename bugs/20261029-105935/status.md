# 20261029-105935: weapon is black, compare to NBlood but looks wrong

Status: fixed

## History

- 2026-10-29 10:59:35 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: the weapon took the sprites' shade, the sky ceiling's under a sky (63 on DWE3M1): black
- 2026-10-29 Fix: the weapon takes its sector's floor shade (MapFaces.WeaponShadeAt), as Blood's view.cpp
