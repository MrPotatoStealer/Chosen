# 20261031-113518: same bug when dooor is moving

Status: fixed
Regression: movers
Regression check: open the door: no long thin line across the yard's floor while it moves

## History

- 2026-10-31 11:35:18 reported in maps/dwe3m1.map
- 2026-10-31 fixed. Cause: The same as 20261031-113433: a floor beside a sliding door moves only some of its corners, and its fill's bands parted across the yard.
- 2026-10-31 Fix: The same fix (WorldMesher.MovesRigidly).
- 2026-10-31 note: correction: the fill shares its corners along its bands, so 'bands parted' is not shown to be the mechanism. Shown: blending a floor that doesn't move rigidly opens the slits (bugs.py motion --selftest), and building it each drawn frame closes them. Covered by bugs.py motion.
