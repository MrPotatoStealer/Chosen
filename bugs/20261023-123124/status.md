# 20261023-123124: lid looks like just a plane, should look like a box

Status: fixed
Regression: room-over-room, movers
Regression check: inside the grave looking up while the lid slides: the lid shows its thick side through the portal

## History

- 2026-10-23 12:31:24 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: Through the room-over-room portal the copy of the live sectors' mesh was refreshed only when its bounding box changed; the lid slides inside the same box, so the portal kept the first frame's geometry (the lid's side missing).
- 2026-10-23 Fix: LiveRenderer stamps its mesh with a version on each rebuild; PortalRenderer.Mirror rebuilds a copy when the version changes.
