# 20261026-093556: some strangeness here with the railing disapearring/appearing when moving

Status: fixed

## History

- 2026-10-26 09:35:56 reported in maps/e1m3.map
- 2026-10-26 fixed. Cause: Not reproduced from a fresh start; most likely the stale occluder of the previous level (fixed with 185719), which hid it by view direction after a level change
- 2026-10-26 Fix: Fixed by freeing the occluder on level load (LiveRenderer.Setup); the user confirmed the railing stays after arriving from another level
