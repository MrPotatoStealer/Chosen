# 20261020-231336: looks like irst projectile from the tesla gun is incorrectly sideways

Status: fixed

## History

- 2026-10-20 23:13:36 reported in maps/e1m7.map
- 2026-10-21 note: ActFireMissile/AltFireTesla/fxTeslaAlt match NBlood; asked the user what looks sideways (the ball's path, its trail, or the sprite).
- 2026-10-21 fixed. Cause: Sprites spawned during a game frame took PrevAng at AddSprite, before actFireMissile set their angle; the renderer interpolated the Tesla bolt's angle from 0 on its first drawn frame and picked a side rotation frame.
- 2026-10-21 Fix: World.Tick sets PrevAng = Ang at the end of the frame for sprites spawned in it (World.cs _spawned).
