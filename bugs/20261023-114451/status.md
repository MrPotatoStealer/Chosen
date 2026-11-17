# 20261023-114451: door won't open

Status: fixed

## History

- 2026-10-23 11:44:51 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: Live sectors' walls are world collision: a push or shot at them found no door to open.
- 2026-10-23 Fix: World.LiveHit: a trace hitting a live wall or plane resolves to its x-wall proxy or its sector's push, as Blood's hitscan.
