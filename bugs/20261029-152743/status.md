# 20261029-152743: trees are cutoff here

Status: fixed
Regression: sprites

## History

- 2026-10-29 15:27:43 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: the trees' sector counted as seen only through a link: the river is also the far room of the links under it, seen all round through them, and that window swallowed what the walk saw of it directly, so the bank and the trees beyond were unmarked and the sky cut them
- 2026-10-29 Fix: the window walked directly is kept apart from the one seen through links (SectorVisibility _dlo/_dhi); two sky ceilings (floors) never shut a wall
