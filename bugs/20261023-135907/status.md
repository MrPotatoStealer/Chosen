# 20261023-135907: strange sides herew

Status: fixed

## History

- 2026-10-23 13:59:07 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: A wall between two planes that cross along it (the floe dipping below the snow at the water end) was clamped per end, leaving a sliver of step face along the whole edge.
- 2026-10-23 Fix: LiveRenderer cuts the face where the planes cross: only the part where the step exists is drawn.
