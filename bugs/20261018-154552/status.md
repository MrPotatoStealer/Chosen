# 20261018-154552: train sector is missing, no collision. same as previous report about not all sectors seems to follow

Status: fixed
Regression: movers
Regression check: call the train: the whole middle car arrives, with collision

## History

- 2026-10-18 15:45:52 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Same as 20261018-154455: the middle car's walls stayed behind, so at the platform only its floor arrived.
- 2026-10-18 Fix: See 20261018-154455.
