# 20261027-202741: should have collision?

Status: fixed

## History

- 2026-10-27 20:27:41 reported in maps/e4m2.map
- 2026-10-27 fixed. Cause: not a bug: the grate is switch sprite 314 with no blocking flag (cstat 0x1d0: hitscan only), NBlood lets you swim through too; shooting it trips the switch
- 2026-10-27 Fix: none
