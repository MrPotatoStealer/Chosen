# 20261105-143410: looks like game can't render two floors over each other. Seems fine in editor

Status: fixed

## History

- 2026-11-05 14:34:10 reported in maps/test.map
- 2026-11-05 fixed. Cause: Links don't nest in the game's portal copies: seen through the bottom room's link, the middle room's own link up (another 504 face in the copy) was painted black, and the three rooms, sharing the ground, hid each other in the stacked-rooms walk
- 2026-11-05 Fix: A link in place (no offset: rooms really over each other, as Add Floor makes them) has its face left out in copies (the room beyond is there), and sectors joined by such links (a chain too) are no stacked partners
