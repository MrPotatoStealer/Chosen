# 20261027-202414: hole in geo

Status: fixed
Regression: movers, masked-walls
Regression check: the gate leaves start closed across the corridor (OFF), floor whole; send 117 on swings them open against the side walls

## History

- 2026-10-27 20:24:14 reported in maps/e4m2.map
- 2026-10-27 fixed. Cause: E4M2's gate leaves 35/37 (617, level with the rooms around them) got no mover entity (no floor/ceiling brushes: the part was dropped), so they never turned to OFF and stood open; their floor area was cut out
- 2026-10-27 Fix: door_entities gives a whole mover with no brushes a driver brush; masked walls on a whole mover are built into it at OFF (mover_masked)
