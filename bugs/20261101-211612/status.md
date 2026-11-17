# 20261101-211612: the dynamic light coming from candles looks a bit off

Status: fixed
Regression: dynamic-lights

## History

- 2026-11-01 21:16:12 reported in maps/mrw2m1.map
- 2026-11-01 fixed. Cause: The disc is Blood's own halo (NBlood draws it too). The dynamic light was the part off: placed low at the candle's base, it lit the candle and its halo beside it, burning them out, and the candle's model shadowed it.
- 2026-11-01 Fix: Candles' and torches' lights at the flame (FlameSpot), weaker for candles, their models cast no shadow for them; halos and fullbright pictures take no dynamic light (light_lit).
