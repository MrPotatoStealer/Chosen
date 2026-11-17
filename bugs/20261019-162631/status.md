# 20261019-162631: sprite shift offset when rendering through portal

Status: fixed
Regression: room-over-room, sprites

## History

- 2026-10-19 16:26:31 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: a sprite straddling a portal plane was drawn only in its own room; through the portal the part beyond the plane showed the sprite unshifted, so its two halves didn't meet
- 2026-10-19 Fix: such a sprite gets a copy moved by the link offset, seen only by the portal cameras; it is hidden and freed with the sprite
