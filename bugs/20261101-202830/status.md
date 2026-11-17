# 20261101-202830: bug happened again, seems to happen when loading a level using command

Status: fixed

## History

- 2026-11-01 20:28:30 reported in maps/e1m2.map
- 2026-11-01 fixed. Cause: Not the sectors: the log was full of "not enough room in the RESOURCES descriptor heap". Each level's materials and pictures kept their D3D12 bindings: sprite materials were never cleared, each add-on loaded a new set of pictures, and dropped world materials waited for the garbage collector. After enough level changes (this session 8, three add-ons) new materials got no binding and were not drawn.
- 2026-11-01 Fix: SpriteRenderer.Setup clears its material caches, Game.StartLevel collects at once (7-16 ms), max_resource_descriptors raised to 262144. Repeating the session's 8 levels three times: at the old limit errors from the 25th load before, none after; usage now flat at about 32000 (DWE1M1). bugs.py regress warns if the heap runs out.
