# 20261026-182743: getting pushed by the water under this bridge when walking on the bridge

Status: fixed

## History

- 2026-10-26 18:27:43 reported in maps/e3m6.map
- 2026-10-26 fixed. Cause: ActApplyPan pushed anything standing on something; Blood (actor.cpp) pushes only a sprite whose bottom reaches its sector's own floor, and the bridge is floor sprites over the flowing water
- 2026-10-26 Fix: No pan push while standing on a sprite (FlorHit is a sprite hit)
- 2026-10-26 fixed. Cause: ActApplyPan pushed anything standing on something; Blood pushes only a sprite whose bottom reaches its sector's own floor, and the bridge is floor sprites over the flowing water
- 2026-10-26 Fix: No pan push while standing on a sprite (FlorHit is a sprite hit)
