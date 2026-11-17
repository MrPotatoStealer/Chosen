# 20261020-173048: massive performance issue here

Status: fixed
Regression: performance
Regression check: play <id>: no IndexOutOfRangeException in the log, monsters move

## History

- 2026-10-20 17:30:48 reported in maps/e1m4.map
- 2026-10-20 fixed. Cause: A blood spurt checked whether its spawn point was inside a brush (PointContents); a brush model with no brushes had a 1x1x1 grid but no cells, so a point in cell (0,0,0) indexed past the end (IndexOutOfRangeException). The exception aborted every game tick from about 1.6 s into E1M4 (frame 192): the player still moved, everything after the event pass (monsters, effects) stood still, and each tick logged a stack trace, which made the frames slow.
- 2026-10-20 Fix: An empty brush model gets an empty grid (BrushModel.Finish), so every point is outside it. Replays of both E1M4 recordings now run to the end without an exception (they differ from the recordings from frame 192 on, where the recorded sessions were stuck).
