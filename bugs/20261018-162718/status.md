# 20261018-162718: sprite on button is too bright

Status: fixed
Regression: sector-lighting

## History

- 2026-10-18 16:27:18 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Same as 20261018-162612: the button's hand print sprites sampled the light grid instead of their sector (Blood: shade -23 + floor 46 = 23).
- 2026-10-18 Fix: See 20261018-162612 (sprite sector shade).
