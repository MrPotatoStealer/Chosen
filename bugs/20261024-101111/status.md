# 20261024-101111: this wall looks incorrect

Status: fixed
Regression: movers
Regression setup: nomonsters | 2
Regression check: the closed sliding panel (ornamented frame) fills the doorway; send s128 on opens it

## History

- 2026-10-24 10:11:11 reported in maps/e1m5.map
- 2026-10-24 fixed. Cause: Sliding door 128's pocket spikes start at the doorway's corners; the converter (live_moves by position) and the game (ReadLiveWalls merging corners by position) moved every wall at a moved point, so the closed pose dragged the doorway's own corners: wall 841 ran diagonally across the doorway and the slab's panel was never seen. Build moves only the marked walls' starts and what DragPoint links to them through two-sided walls.
- 2026-10-24 Fix: Converter drag_walls: NBlood's DragPoint closure, the .live 'm' written per wall; the game keys a live corner by position and mover. Also fixes E1M5's door 119 and nine E1M6 sliding doors with the same layout; live sectors over one another, overlapping in height, are now stacked pairs (E2M5's bookcase over 472: its painting showed through).
