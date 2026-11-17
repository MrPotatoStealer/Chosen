# 20261030-145434: difficulty walk out this door, seem to get stuck on the side for a bit

Status: fixed

## History

- 2026-10-30 14:54:34 reported in mods/dw/maps/dwe3m1.map
- 2026-10-30 note: The player stops one clip distance (12 units, clipdist 48) from the doorway's left jamb (wall 7992, x -13696) and slides 0.6 units a frame along the box corner until clear: Build's clipmove pads walls with a square box, a 48-unit doorway leaves a 24-unit strip for the centre. As NBlood's clipmove does - to compare in NBlood: spot dwe3m1-door
- 2026-10-30 fixed. Cause: Not a bug: Build's clipmove pads walls with the player's square clip box, so the 48-unit doorway leaves a 24-unit strip; NBlood behaves the same (checked by the user)
- 2026-10-30 Fix: None needed
