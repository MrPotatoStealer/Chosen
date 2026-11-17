# 20261019-181718: no shadows even when shadow is turned on. I don't expect to see the light go thru the wall like this

Status: fixed
Regression: room-over-room, dynamic-lights

## History

- 2026-10-19 18:17:18 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: the lamp's copy for the yard (the yard portal's rooms are stacked, offset only 32/-32/16) sat inside the upper area beside the passage and, being shadowless, lit its walls through the stone; also only the nearest 8 lights had shadows
- 2026-10-19 Fix: a copy lights only its linked room (the other portal side's footprint beyond its plane, known to the shaders) and the light itself only outside it; every light casts shadows, keeping its light node so still lights don't redraw shadow maps
