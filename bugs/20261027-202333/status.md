# 20261027-202333: door missing collision

Status: fixed
Regression: collision
Regression check: walk into the shut sliding door: it blocks (phys_sectors 1 and 0); send 104 on, then it lets you through

## History

- 2026-10-27 20:23:33 reported in maps/e4m2.map
- 2026-10-27 fixed. Cause: E4M2's sliding door 152 (614 beside rooms sharing space) isn't live; Build's collision took its walls as stored (open)
- 2026-10-27 Fix: BuildClip moves a non-live marked slide/turn's corners with the part of their sign (partSign key, DragPoint), so the shut door blocks
