# 20261030-090034: some issues here with flickering when rebuilding meshes

Status: fixed

## History

- 2026-10-30 09:00:34 reported in maps/e1m8.map
- 2026-10-30 fixed. Cause: Live tiles' blend between game frames accepted corners paired up to the sum of every mover's reach: E1M8's twisting corridor (16 sectors turning) let it reach 115 units, across the corridor, so other faces' corners paired and slivers of wall were drawn between the two frames
- 2026-10-30 Fix: Pairs takes the two movers that carry furthest, not all of them (accepted moves now at most 2 units there); r_lerpcheck 2 prints each blend's furthest corner
