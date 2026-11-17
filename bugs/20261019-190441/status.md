# 20261019-190441: where are all the enemies?

Status: fixed

## History

- 2026-10-19 19:04:41 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: b3d_monsters was 0: left over from Claude's performance test runs, which set it and didn't restore it
- 2026-10-19 Fix: restored b3d_monsters 1 (all 123 monsters are in the converted map)
