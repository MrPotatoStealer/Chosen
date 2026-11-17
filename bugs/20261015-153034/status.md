# 20261015-153034: Incorrect texture, should be sky, instead its some sort of wood

Status: fixed
Regression: room-over-room

## History

- 2026-10-15 15:30:34 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: The graveyard pit is two stacked sectors joined by a Blood stack link: 65 (lower, -112..32) and 90 (upper, 48..176). 65 is connected to the rest of the map so it was left in place, keeping 65's portal ceiling (tile 504, looks like wood) and a 32-unit misalignment between the halves.
- 2026-10-15 Fix: convert_map.py drag_link(): when a linked lower sector can't be moved as an island, its outline is dragged by the link offset (shared corners pull neighbouring walls along), validated for invalid shapes and overlaps; the gap between the two halves is opened (Converter.air extends the lower open space to the upper floor).
