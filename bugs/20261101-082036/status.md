# 20261101-082036: make the napalm explosion light up a big area, fast in slow out

Status: fixed

## History

- 2026-11-01 08:20:36 reported in maps/e2m1.map
- 2026-11-01 fixed. Cause: An explosion light faded in 0.6 s with a reach of 320 units times its sprite size, gone with the sprite.
- 2026-11-01 Fix: A napalm burst is its own light (DynamicLights.AddBurst): 1100 units, up in 0.05 s, dying away over 1.8 s, outliving the sprite, kept among the nearest lights. Seen in the replayed report and measured from a still camera with boom napalm.
