# 20261031-230247: mist pixel size don't show up until you leave menu and come back

Status: fixed

## History

- 2026-10-31 23:02:47 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: The mist's size was only sent from Game's frame, which doesn't call GroundMist.Update while the menu is up.
- 2026-10-31 Fix: Changing the mist's mode, size or density updates it at once.
