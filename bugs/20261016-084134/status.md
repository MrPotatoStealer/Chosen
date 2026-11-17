# 20261016-084134: texture is incorrect scale here, texture should scale when opening the curtains to simulate the folds getting smaller.

Status: fixed
Regression: movers
Regression check: open the curtains: the texture squeezes into folds

## History

- 2026-10-16 08:41:34 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: The curtain panel took the stretching wall's texture mapped to its 4-unit open length (192 texels over 4 units); Build maps xrepeat*8 texels across the wall's current length.
- 2026-10-16 Fix: Stretching walls of sweeps are blood_stretchwall entities drawn by the client at their current length with Build's mapping (folds squeeze as the curtain opens); panel faces along them are caulk.
