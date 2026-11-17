# 20261031-142902: ground fog disappearing in some angles

Status: fixed

## History

- 2026-10-31 14:29:02 reported in maps/e3m1.map
- 2026-10-31 fixed. Cause: The mist is a big Box fog volume per area. Looking up a little (E3M1's street at pitch -27, all right at -15) the whole volume was left out, floor still in view: most likely Godot's bounds for a volume whose corners lie behind the camera.
- 2026-10-31 Fix: GroundMist: World shape volumes (the floor map keeps the mist to its area), each hidden while its box is wholly out of the view (InView). Costs about 0.4 ms of GPU on DWE1M1 and DWE3M1 at 3440x1440.
