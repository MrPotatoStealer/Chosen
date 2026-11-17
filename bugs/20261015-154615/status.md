# 20261015-154615: should be darker like the surrounding area in the grave

Status: fixed

## History

- 2026-10-15 15:46:15 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: bsp_shade.py matched a vertical face to a Build wall by 2D position only; where areas are stacked (the coffin moved under the grave) a wall of the room above lies on the same line and its shade was used.
- 2026-10-15 Fix: bsp_shade.py wall_shade() prefers walls whose sector is open at the face's height (a wall is only seen from inside its own sector).
