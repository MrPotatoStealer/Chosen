# 20261117-111050: performance issues standing here

Status: fixed
Regression: performance
Regression setup: "" | 3
Regression check: r_perfoverlay 1: game total about 1.5 ms (was 10), sprites in r_hitchlog about 1 ms (was 8)

## History

- 2026-11-17 11:10:50 reported in maps/sevict.map
- 2026-11-17 fixed. Cause: Portal copies of sprites through a floor or ceiling link that isn't water had no visibility test: Eviction's floor link 5 (to sector 292, a 128x448 hole) has a copy region covering most of the level, so about 1000 sprites were copied every frame (7.8 ms of the 8.6 ms sprite pass, 1.36 M primitives), nearly none seen through the hole. Also the procedural fire (tile 2342) wrote its texture with 16384 Image.SetPixel calls every 25 ms (about 2 ms each time). Editor runs load Godot's Debug GodotSharp.dll (JIT optimisation off), which made both several times slower than in the exported build.
- 2026-11-17 Fix: SpriteRenderer: every floor/ceiling link's copies tested by SeenThrough (the sprite's box, its picture's size at least, projected from the eye onto the link plane must meet the face; water keeps its 1024 reach and 64 box). ProceduralFire fills a byte array and hands it over with one Image.SetData. At the spot: game CPU 10.2 to 1.4 ms per frame, sprites 8.4 to 1.0 ms, primitives 1.36 M to 0.39 M.
