# 20261015-232559: lightswitctes are not working, should turn on lights on interaction

Status: fixed
Regression: sector-lighting
Regression check: the light switch turns the rooms' lights on and off

## History

- 2026-10-15 23:25:59 reported in maps/e1m1.bsp
- 2026-10-16 fixed. Cause: the switch works as scripting (channel 124 ramps the busy value of nine rooms), but Blood's DoSectorLighting wasn't implemented and shading is baked in lightmaps
- 2026-10-16 Fix: faces of light-effect sectors become blood_sectorfx skins; sectorfx.qc runs DoSectorLighting each frame and sets the skin brightness
