# 20261031-113433: gaps in water geo when boat is moving

Status: fixed
Regression: movers, water
Regression check: ride the boat and open doors: no long thin lines across the water or the floor beside them while they move

## History

- 2026-10-31 11:34:33 reported in maps/dwe3m1.map
- 2026-10-31 fixed. Cause: From this session's performance work: moving sectors were built once a game frame and blended in the shader. A floor's fill (PolyFill) is bands straight across the sector; with a moving hole in it (river sector 538 round the boat) the blend moved the hole's corners and not the far edges, so the bands parted along their whole length: long slits.
- 2026-10-31 Fix: WorldMesher.MovesRigidly: only a sector moving rigidly is blended (a blend of two rigid places keeps lines straight); any other is built each drawn frame at the blend, as before.
- 2026-10-31 note: correction: the fill shares its corners along its bands, so 'bands parted' is not shown to be the mechanism. Shown: blending a floor that doesn't move rigidly opens the slits (bugs.py motion --selftest), and building it each drawn frame closes them. Covered by bugs.py motion.
