# 20261019-181857: flickering looks strange, i like the idea of clickering but its not correct

Status: fixed
Regression: dynamic-lights
Regression check: the flicker is smooth, not a new random level every frame

## History

- 2026-10-19 18:18:57 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the flicker was a new random energy every drawn frame (jitter)
- 2026-10-19 Fix: smooth value noise per light (about 6 and 15 Hz mixed), its own phase per sprite
