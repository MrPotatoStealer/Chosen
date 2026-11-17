# 20261023-083949: iceblock should be bobing before stepping on it

Status: fixed

## History

- 2026-10-23 08:39:49 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The water strip's bob was held at zero while shut (to keep it under the snow skin), and the floe's AlignSlopes read that held height, so the floe stayed flat until stepped on.
- 2026-10-23 Fix: AlignSlopes reads Build's raw bob (Sector.BobRaw); shut, the shown water bob only dips (min(raw,0)) so it stays under the skin.
