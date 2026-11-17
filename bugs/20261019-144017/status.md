# 20261019-144017: this wall should not be flashing

Status: fixed
Regression: sector-lighting, movers

## History

- 2026-10-19 14:40:17 reported in maps/e1m2.map
- 2026-10-19 fixed. Cause: a lit z-motion door's mover took the light effect on every face; Blood shades only the door sector's own floor and ceiling, its side faces are the neighbours' walls
- 2026-10-19 Fix: MoverRenderer lights only a mover's floor/ceiling faces (and the walls of whole-sector movers)
