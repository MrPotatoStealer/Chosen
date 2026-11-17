# 20261019-143800: ground to bright?

Status: fixed
Regression: sector-lighting

## History

- 2026-10-19 14:38:00 reported in maps/e1m2.map
- 2026-10-19 fixed. Cause: measured against NBlood at six places: Godot's distance darkening was twice NBlood's (the DarkPlaces-era constant); the floor here was darker than NBlood, not brighter
- 2026-10-19 Fix: build_visibility = visibility / 1048576 (r_ambientlight scales it like NBlood's Visibility option)
