# 20261028-085030: sprite on bridge seems to be moving when camera is moving creating a jitter effect

Status: fixed
Regression: sprites, room-over-room

## History

- 2026-10-28 08:50:30 reported in maps/e3m3.map
- 2026-10-28 fixed. Cause: The bridge is floor sprites 8 units past link 50's plane; the reach-through test (a sprite straddling a portal plane is drawn unclipped) allowed 24 units past it, so the sprite and its portal copy, 2 units apart by the link's offset, were both drawn and fought
- 2026-10-28 Fix: SpriteRenderer.ReachesThrough: a flat sprite reaches through only lying within a unit of the plane
