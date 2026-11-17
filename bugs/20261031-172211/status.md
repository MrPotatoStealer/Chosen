# 20261031-172211: napalm sprite get stuck in portal after firing

Status: fixed
Regression: sprites, water
Regression check: fire napalm into the water: no ball left hanging on the surface

## History

- 2026-10-31 17:22:11 reported in maps/e2m1.map
- 2026-10-31 fixed. Cause: The ball wasn't stuck: the missile flew on and blew up in the far room. Its picture stayed: going down through E2M1's lake its place jumped to the far room 4352 units away, out of view, and SpriteRenderer leaves an out-of-view sprite's node as it is (a performance shortcut, assuming the node is out of view too). The node stayed at the surface, in view, until the missile was gone. Any sprite moved out of view at once could do this.
- 2026-10-31 Fix: SpriteRenderer.Draw: a sprite out of view that moved since it was placed (Sprite.DrawnAt) has its nodes hidden.
