# 20261115-161600: check if we can increase performance here

Status: fixed
Regression: performance
Regression check: frame time in the hall with the overlay: GPU about 1 ms less than with r_ao_half 0

## History

- 2026-11-15 16:16:00 reported in maps/sstation.map
- 2026-11-15 fixed. Cause: At 3440x1440 the frame is the GPU's, per pixel: of 7.5 ms the ambient occlusion pass (GTAO, every pixel: 36 depth samples and a 5x5 blur) took 2.0, the depth pre-pass 1.6 (0.5 of it the stacked rooms' walk), the opaque pass 1.75, the fog's blur 0.7, the mist volumes 0.6, glow 0.35; shadows, lights and the voxel models' 2 million triangles cost little.
- 2026-11-15 Fix: r_ao_half (default 1; Enhanced > Lighting, AO RESOLUTION): the occlusion found for every second pixel each way and spread by depth as it is blurred (0.8 ms, the picture all but the same); the stacked rooms' walk left out for a pixel outside the bounds of the sectors sharing its own's ground; the fog's blur a box of low quality. About 6.4 ms in all there (Godot's --gpu-profile; r_stackwalk 0 shows the walk's cost).
