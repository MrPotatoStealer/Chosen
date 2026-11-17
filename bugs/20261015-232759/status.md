# 20261015-232759: secret is revelead message is triggered before finding the secret (behind this object)

Status: fixed

## History

- 2026-10-15 23:27:59 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: sector membership used the volume's bounding box; secret sector 114 is a triangle, so standing beside it counted as entering
- 2026-10-15 Fix: converter writes sector outlines (maps/<map>.sec); sectorAtPoint tests the point against the outline (even-odd)
