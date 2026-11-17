# 20261102-083959: lamps are only half lit, half bright and half dull

Status: fixed
Regression: dynamic-lights

## History

- 2026-11-02 08:39:59 reported in maps/e1m1.map
- 2026-11-02 fixed. Cause: The lamp post's light sits a few units behind the lantern's glass; its voxel model only left it out of its shadow casters, so the pane facing the light burned bright and the one beside it stayed dull (which one depends on where you stand).
- 2026-11-02 Fix: Fixture lights no longer light the fixtures' models (LightCullMask on render layer 3, as their shadow caster mask): the lantern shaded as Blood shades it, even, like NBlood. The moon, weapons and explosions still light it.
