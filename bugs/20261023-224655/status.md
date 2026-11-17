# 20261023-224655: performance issue at this spot as well

Status: fixed
Regression: performance

## History

- 2026-10-23 22:46:55 reported in maps/e1m8.map
- 2026-10-23 fixed. Cause: Each portal drawn re-copied every mirrored moving mesh that changed, and E1M8's live sectors (the pulsing walls and others, rebuilt every frame) were mirrored for every portal whatever their place: 12-15 ms a frame copying them for the walkway's floor portal
- 2026-10-23 Fix: PortalRenderer mirrors a moving mesh only while its box beyond the plane, seen from the eye, falls on the portal's face (else hidden and not rebuilt): 0.1 ms there. The game tick still costs ~6 ms there (ProcessMotion of the pulsing corridor's ~20 movers)
