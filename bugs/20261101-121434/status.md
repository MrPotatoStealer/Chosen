# 20261101-121434: AO needs to take underwater fog intro consideration just like normal fog

Status: fixed

## History

- 2026-11-01 12:14:34 reported in maps/e2m1.map
- 2026-11-01 fixed. Cause: Occlusion darkened the emission after the water fog was mixed in, so the fog itself was darkened.
- 2026-11-01 Fix: World, sprite and voxel shaders apply occlusion before WaterFx.Apply: it fades with the water fog and the fog colour is never darkened.
