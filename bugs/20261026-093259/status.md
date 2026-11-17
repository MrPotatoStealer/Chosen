# 20261026-093259: carousel is not rotating

Status: fixed

## History

- 2026-10-26 09:32:59 reported in maps/e1m4.map
- 2026-10-26 fixed. Cause: Not a game bug: the e1m4-carousel test spot stood inside the carousel room, sector 352, whose enter trigger (channel 128, once) starts the carousel. Build fires enter only on a sector change, so starting there never fires it (NBlood the same).
- 2026-10-26 Fix: Test spot moved outside the room's south doorway (sector 354): walk in and it turns.
