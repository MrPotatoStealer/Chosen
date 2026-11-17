# 20261101-211805: same here dynamic light is a bit off from the actual lantern

Status: fixed
Regression: dynamic-lights

## History

- 2026-11-01 21:18:05 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: The lantern's light sat right beside its own picture, so a hot spot burned on the picture off centre from the glass.
- 2026-11-01 Fix: Fullbright pictures take no dynamic light (light_lit), and nor do halos.
