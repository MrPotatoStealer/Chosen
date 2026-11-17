# 20261030-191444: invisible collision

Status: fixed

## History

- 2026-10-30 19:14:44 reported in maps/e2m1.map
- 2026-10-30 note: Cause found: the player is stuck in the strip of lake 125 the ice block 113 slid out of. Live sector 125's hole loop round 113 keeps its stored corners (LiveContains, InSectorNow), so the vacated area is in no sector and ClipMoveSectors throws the move away (s2 < 0). Build drags those corners with the block (DragPoint). Same root as 20261030-171348 (fixed in the sweep only).
- 2026-10-30 fixed. Cause: LiveContains kept the converted live sector's stored hole loop, so the move into it was discarded.
- 2026-10-30 Fix: Native level: Build's clipmove on the board's walls as they are now (no live-sector loops).
