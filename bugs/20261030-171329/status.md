# 20261030-171329: performance here is terrible

Status: fixed

## History

- 2026-10-30 17:13:29 reported in maps/e2m1.map
- 2026-10-30 fixed. Cause: The water portals mirrored every live tile of the lake (empty at load, so kept whatever their place): the lake's floor bobs, so its 119 tiles were rebuilt and copied again every frame for the portal drawn, about 50 ms
- 2026-10-30 Fix: PortalRenderer.Mirror: a mesh rebuilt in place (a live tile) is mirrored only within 256 units of the face's footprint beyond it, a mover within twice the copy's reach; 70 ms frames there down to about 15 (the lake's tiles themselves still rebuild each game frame, about 6 ms)
