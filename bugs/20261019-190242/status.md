# 20261019-190242: missing ceiling

Status: fixed
Regression: stacked-rooms

## History

- 2026-10-19 19:02:42 reported in maps/e1m3.map
- 2026-10-19 fixed. Cause: the room under E1M3's upper corridors (ceiling 56) overlaps their floors (32) in height; the converter lowered its ceiling to the corridor floor there (a box hanging from the ceiling), and the corridors' wall blockers hung into it
- 2026-10-19 Fix: where stacked rooms overlap in height: the upper floor stays a thin solid slab unseen from below, the lower ceiling is drawn at its height as a non-solid one-sided face (blood_flat); band walls are seen only from their own room's height (blood_above/blood_below: the world shader discards them by camera height); no blocker on a two-sided wall whose neighbour is open across the band. A small grey piece of an upper-room structure can still show near the ceiling there
