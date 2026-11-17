# 20261026-180722: some glitches here going down into the water sometimes, like 1 frame the wrong stuff is rendered

Status: fixed
Regression: water, room-over-room
Regression check: walk along the flooded stairs with the eye just over the surface: the room above never drops out for a frame

## History

- 2026-10-26 18:07:22 reported in maps/e3m3.map
- 2026-10-26 note: 2026-10-26: the view's sector at a link is now the player's tracked sector, the far side's while the eye is past the plane (PlayerView.EyeSector): please check whether the one-frame glitch entering the water is gone.
- 2026-10-28 note: 2026-10-28 user: still there; all geo above the water stops rendering for a frame, only while moving, maybe crossing sectors on the stairs under the water
- 2026-10-28 note: 2026-10-28: the flooded stairs are a link a step (61-71, same plane and offset); the eye, interpolated, trails the body, and at a step's edge they were over different links, so no view from the room above and the void past the plane drew for a frame. PlayerView.LinkViewOrigin now takes the body over a sibling link of the same surface. Please check by walking the stairs with the eye at the surface (playat e3m3-stairs).
- 2026-10-28 fixed. Cause: Standing (or crouch-swimming) on the top flooded step, sector 65 (16 units tall), the step's floor lifts the body's origin above the water plane while the body is still in the underwater room for a tick; the view took the room above only with the feet still on the near side, so that tick drew the void over the water. (Crouching in the room above sinks the short crouch sprite back through: the loop between the rooms.)
- 2026-10-28 Fix: PlayerView.LinkViewOrigin: Blood's viewDrawScreen, the eye past a link plane of the body's Build sector views from the room beyond, wherever the feet are
