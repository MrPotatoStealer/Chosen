# 20261015-155049: incorrect texture, should be same as the walls next to it

Status: fixed
Regression: room-over-room, geometry

## History

- 2026-10-15 15:50:49 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: The band between the dragged pit's lower ceiling and upper floor took the lower sector's wall texture.
- 2026-10-15 Fix: Converter.air(): the band belongs to the upper sector (link_gap_up), so its walls continue down.
