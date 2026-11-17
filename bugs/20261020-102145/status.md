# 20261020-102145: portal still messed up here

Status: fixed
Regression: stacked-rooms

## History

- 2026-10-20 10:21:45 reported in maps/e1m3.map
- 2026-10-20 fixed. Cause: Which of two height-overlapping rooms shows was one switch per face, decided by which room was fewer doorway steps from the camera's sector; here the lower room (sector 31) and the upper corridor (161) tied at 5 steps, so the lower room's walls showed through the stair opening.
- 2026-10-20 Fix: Stacked-room faces now do Build's sector walk per pixel: from the camera's sector through the doorways along the line to the pixel, hidden where the walk arrives in a room stacked with the face's own (shader + stack_walls texture; sprites on the CPU). The step count is only the fallback.
