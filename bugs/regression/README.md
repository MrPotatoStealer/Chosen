# Regression categories

Tricky bugs grouped by the code they exercise (the bugs themselves stay in bugs/<id>/). After changing that code run `python pipeline/bugs.py regress <category>`: it retakes each bug's view in the Godot game and flags the ones that differ from the accepted baseline; `bugs.py accept <id>` makes a checked new look the baseline. Tag a bug with `bugs.py tag <id> <category>`.

| Category | Bugs | Covers |
|---|---|---|
| [room-over-room](room-over-room/README.md) | 40 | Room-over-room links (Blood stack links): portal rendering and alignment (PortalRenderer, World.NativeLinks, the eye crossing links in Game.cs), sprites and lights seen through portals. |
| [stacked-rooms](stacked-rooms/README.md) | 45 | Rooms stacked so they overlap in height, without a link: StackedRooms.cs (the walk), SectorMap.FindOverlaps, sprites hidden by their Build sector. |
| [movers](movers/README.md) | 74 | Doors, lifts, sliding/rotating/z-motion sectors: trInit and the busy procedures (World.NativeSectors), the moving sectors' meshes and planes (Render.Native). |
| [carried-sprites](carried-sprites/README.md) | 7 | Sprites that move with sectors (cstat 8192/16384, moveZPart/zPart, turntables) and sprites that wait inside another sector. |
| [sector-lighting](sector-lighting/README.md) | 19 | Build shading: sector shades, light effects and switches (World.SectorFx, the native world's b3d_sector data, BuildShading), sprite shading, overall brightness against NBlood. |
| [dynamic-lights](dynamic-lights/README.md) | 15 | DynamicLights.cs: fixture lights, their switches, shadows, copies through portals, performance. |
| [sprites](sprites/README.md) | 27 | SpriteRenderer and voxels: offsets, one-sided and wall sprites, translucency, procedural fire, flips. |
| [masked-walls](masked-walls/README.md) | 17 | Masked, translucent and one-way walls (cstat 16/32/128/512), palookup hints, double-sided faces. |
| [geometry](geometry/README.md) | 49 | The native world mesh (Render.Native.WorldMesher, WorldShader): holes, cracks and seams, walls between sloped planes, texture pegging, sky. |
| [collision](collision/README.md) | 39 | Player/actor collision (Sim/Collision): blocking walls and sprites, ledges, link crossing. The screenshot only finds the spot: retest by playing there (or bugs.py replay). |
| [performance](performance/README.md) | 19 | Frame rate: portal culling, occlusion, hitscan traces, shadow map updates. The screenshot only finds the spot: retest by looking there with perf. |
| [water](water/README.md) | 10 | Water and other liquids: entering and leaving through water links, underwater sectors (World.Physics CheckLink, InUnderwaterSector), swimming, the screen palettes (ViewPalette). The screenshot only finds the spot: retest by playing the recording (bugs.py replay) and looking at the end. |
