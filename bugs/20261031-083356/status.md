# 20261031-083356: can't select episodes in main menu

Status: fixed

## History

- 2026-10-31 08:33:56 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: The episode menu (and the next-level check at a level's end) looked for the converted res://maps/<name>.map files the cleanup removed.
- 2026-10-31 Fix: Menu and GoNextLevel check the Blood .MAP (Addons.BuildMapPath).
