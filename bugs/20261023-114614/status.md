# 20261023-114614: curtains wont open

Status: fixed

## History

- 2026-10-23 11:46:14 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: The curtain's push walls stretch from a fixed corner; only walls starting at a moving corner got the push proxy, and LiveHit looked proxies up by the wall's own name.
- 2026-10-23 Fix: Every wall touching a moving corner gets the part's proxy; .live records each wall's proxy name.
