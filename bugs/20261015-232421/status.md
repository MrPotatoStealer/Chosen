# 20261015-232421: curtains should be closed at start and open when interacting

Status: fixed
Regression: movers
Regression check: curtains closed at start, open when used

## History

- 2026-10-15 23:24:21 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the curtains are deforming edges with solid behind them (stretching walls); the converter kept such doors open
- 2026-10-15 Fix: new sweep panels: a rigid panel over the area the edge sweeps, built closed, slides into the solid behind; faces use the stretching wall texture
