# 20261015-155131: this area is all skewed for some reason, it should be straight.

Status: fixed

## History

- 2026-10-15 15:51:31 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: drag_link moved only the pit sector's outline, skewing its small neighbours 145/154.
- 2026-10-15 Fix: drag_link moves the dead-end branches reachable only through the linked sector rigidly with it (dead_end_group); only corners shared with the rest of the map stretch.
- 2026-10-26 note: Untagged 2026-10-26: its DarkPlaces-era spot lies outside every Build sector (12 units past crypt 65's wall, under graveyard 90's floor); with Build's sector tracking the view there is arbitrary.
