# 20261027-202920: another window collision sill there bug

Status: fixed

## History

- 2026-10-27 20:29:20 reported in maps/e4m3.map
- 2026-10-27 fixed. Cause: same as 20261027-202000: E4M3's glass 205/209 kept its blocking flags after breaking
- 2026-10-27 Fix: SetupGibWallState in OperateWall (see 20261027-202000)
