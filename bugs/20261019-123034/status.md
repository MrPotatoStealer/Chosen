# 20261019-123034: fullscreen effects when taking damage/ picking up items does not take up the full screen but only part of it

Status: fixed

## History

- 2026-10-19 12:30:34 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the screen tint rectangles kept the HUD's size from before the window went fullscreen
- 2026-10-19 Fix: the tints are sized to the viewport every frame
