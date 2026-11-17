# 20261023-114439: missing geo on door sides

Status: fixed

## History

- 2026-10-23 11:44:39 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: The door panel's strip that sticks out of its wall pocket into the doorway is outside the sector at both ends of its travel, so it was in neither the live region nor drawn by the static world (which filled it as open).
- 2026-10-23 Fix: The live region takes in the sweep of each moving wall run (whole panel outline, both ends); the static world uses the even-odd area of live sectors.
