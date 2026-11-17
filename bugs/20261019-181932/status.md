# 20261019-181932: dynamic lights is on even when the light switch is off

Status: fixed
Regression: dynamic-lights
Regression check: with the switch off the ceiling panels give no dynamic light

## History

- 2026-10-19 18:19:32 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the ceiling panels (2290 switches, not fullbright) gave light regardless of the room's light switch, which drives the sector's light effect
- 2026-10-19 Fix: a fixture drawn in its sector's light is as lit as the sector's light effect (World.FxLightLevel): off with the switch, flickering with the room
