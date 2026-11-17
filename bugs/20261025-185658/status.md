# 20261025-185658: wall with no collision, lift? compare to NBlood

Status: fixed
Regression: movers, geometry
Regression setup: send s8 on | 3
Regression check: the open north door shows the lit cabin interior (NBlood with sector 8 on); walking in, the cabin's walls block

## History

- 2026-10-25 18:56:58 reported in maps/e3m5.map
- 2026-10-25 fixed. Cause: The lift cabin (Build 14) and the ring around it (13, marked walls) turn 180 degrees and are stored at ON. The sector renderer drew the ring's walls where they are stored, so with the cabin at OFF and the north door open, its back stood in the doorway: a drawn wall without collision (the ring mover's real faces were behind it).
- 2026-10-25 Fix: The converter flags a moving solid's walls ('mov', moving_walls of non-live sectors); the sector renderer leaves them to the mover, as it does whole movers' walls.
- 2026-10-25 note: Refactor: moving sectors are drawn from Build's walls (r_sectors): the ring's walls (mov) and the cabin (whole sector 14) are drawn where their movers have them now, rebuilt as they turn; the mover brushes only collide.
