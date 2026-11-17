# 20261019-113058: curtains are not rendered correctly

Status: fixed
Regression: movers

## History

- 2026-10-19 11:30:58 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: blood_stretchwall (curtains' walls stretching as they open, drawn by the DarkPlaces client) had no Godot renderer
- 2026-10-19 Fix: Render/StretchRenderer: each stretching wall is a quad from its fixed end to the end riding on its mover, mapped like Build at its current length
