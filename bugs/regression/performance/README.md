# Regression: performance

Frame rate: portal culling, occlusion, hitscan traces, shadow map updates. The screenshot only finds the spot: retest by looking there with perf.

19 bug(s), 1 in the last run. Rerun: `python pipeline/bugs.py regress performance` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261019-104238](../../20261019-104238/status.md) framerate issue/stuttering when shooting and looking. Check recording.

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261019-104238`. Check: shooting and looking around: no stutter (perf)

[report](../../20261019-104238/screenshot.jpg) [NBlood](../../20261019-104238/nblood.jpg) [baseline](../../20261019-104238/baseline.jpg) [now](../../20261019-104238/regress.jpg)

### [20261019-104330](../../20261019-104330/status.md) secondary fire on weapon causing massive stutter

e1m1, same 2026-11-15 17:14 (0.04). `gotobug 20261019-104330`. Check: alternate fire: no stutter (perf)

[report](../../20261019-104330/screenshot.jpg) [NBlood](../../20261019-104330/nblood.jpg) [baseline](../../20261019-104330/baseline.jpg) [now](../../20261019-104330/regress.jpg)

### [20261019-184126](../../20261019-184126/status.md) performance issues due to dynamic lights even when I cant see any?

e1m2, same 2026-11-17 11:51 (0.01). `gotobug 20261019-184126`. Check: perf with dynamic lights and shadows on

[report](../../20261019-184126/screenshot.jpg) [NBlood](../../20261019-184126/nblood.jpg) [baseline](../../20261019-184126/baseline.jpg) [now](../../20261019-184126/regress.jpg)

### [20261019-190113](../../20261019-190113/status.md) opening this vent caude massive stutter

e1m3, same 2026-11-15 17:14 (0.11). `gotobug 20261019-190113`. Check: open the vent: no stutter (perf)

[report](../../20261019-190113/screenshot.jpg) [NBlood](../../20261019-190113/nblood.jpg) [baseline](../../20261019-190113/baseline.jpg) [now](../../20261019-190113/regress.jpg)

### [20261019-205912](../../20261019-205912/status.md) performance issue looking this way

e1m3, same 2026-11-17 11:24 (0.00). `gotobug 20261019-205912`. Check: perf looking this way (portal behind walls not drawn)

[report](../../20261019-205912/screenshot.jpg) [NBlood](../../20261019-205912/nblood.jpg) [baseline](../../20261019-205912/baseline.jpg) [now](../../20261019-205912/regress.jpg)

### [20261020-101750](../../20261020-101750/status.md) still seeing performance issues this with dynamic lights off

e1m2, same 2026-11-15 17:14 (0.02). `gotobug 20261020-101750`. Check: replay or play the station fight with r_perfoverlay 2: no tall bars, ticks under ~6 ms

[report](../../20261020-101750/screenshot.jpg) [NBlood](../../20261020-101750/nblood.jpg) [baseline](../../20261020-101750/baseline.jpg) [now](../../20261020-101750/regress.jpg)

### [20261020-173031](../../20261020-173031/status.md) some massive spikes one just happend before going into this room

e1m4, same 2026-11-15 17:14 (0.04). `gotobug 20261020-173031`. Check: play <id>: no IndexOutOfRangeException in the log, monsters move

[report](../../20261020-173031/screenshot.jpg) [baseline](../../20261020-173031/baseline.jpg) [now](../../20261020-173031/regress.jpg)

### [20261020-173048](../../20261020-173048/status.md) massive performance issue here

e1m4, same 2026-11-15 17:14 (0.08). `gotobug 20261020-173048`. Check: play <id>: no IndexOutOfRangeException in the log, monsters move

[report](../../20261020-173048/screenshot.jpg) [baseline](../../20261020-173048/baseline.jpg) [now](../../20261020-173048/regress.jpg)

### [20261020-231817](../../20261020-231817/status.md) very bad performance

e1m6, same 2026-11-15 17:14 (0.01). `gotobug 20261020-231817`. Check: play through E1M6 with monsters: no stutter (game tick was ~10 ms every frame, AI line-of-sight checks)

[report](../../20261020-231817/screenshot.jpg) [baseline](../../20261020-231817/baseline.jpg) [now](../../20261020-231817/regress.jpg)

### [20261020-232025](../../20261020-232025/status.md) very bad performance

e1m4, same 2026-11-15 17:14 (0.04). `gotobug 20261020-232025`. Check: stand by the carousel while it turns: steady frame rate (game tick t.busy ~2 ms, was 20 ms)

[report](../../20261020-232025/screenshot.jpg) [baseline](../../20261020-232025/baseline.jpg) [now](../../20261020-232025/regress.jpg)

### [20261023-224655](../../20261023-224655/status.md) performance issue at this spot as well

e1m8, same 2026-11-15 17:14 (0.03). `gotobug 20261023-224655`

[report](../../20261023-224655/screenshot.jpg) [baseline](../../20261023-224655/baseline.jpg) [now](../../20261023-224655/regress.jpg)

### [20261025-185719](../../20261025-185719/status.md) very messed up rendering from inside the lift

e3m5, same 2026-11-15 17:14 (0.01). `gotobug 20261025-185719`, setup `send s8 on | 3`. Check: from inside the cabin the corridor north of the open door shows, no lava room

[report](../../20261025-185719/screenshot.jpg) [NBlood](../../20261025-185719/nblood.jpg) [baseline](../../20261025-185719/baseline.jpg) [now](../../20261025-185719/regress.jpg)

### [20261028-123952](../../20261028-123952/status.md) performance here is not great, look into it

dwe1m3, same 2026-11-15 17:14 (0.11). `gotobug 20261028-123952`. Check: walk the start area of DWE1M3 with the overlay on: no drops below 140 fps from the looping raft floors

[report](../../20261028-123952/screenshot.jpg) [baseline](../../20261028-123952/baseline.jpg) [now](../../20261028-123952/regress.jpg)

### [20261029-230637](../../20261029-230637/status.md) look at performance here

dwe3m1, same 2026-11-15 17:14 (0.00). `gotobug 20261029-230637`

[report](../../20261029-230637/screenshot.jpg) [baseline](../../20261029-230637/baseline.jpg) [now](../../20261029-230637/regress.jpg)

### [20261101-135005](../../20261101-135005/status.md) lots of frame stuttering here

cp01, same 2026-11-15 17:14 (0.00). `gotobug 20261101-135005`. Check: replay at --speed=1 with r_hitchlog 0.01: no steady rhythm of slow frames every 0.133 s

[report](../../20261101-135005/screenshot.jpg) [baseline](../../20261101-135005/baseline.jpg) [now](../../20261101-135005/regress.jpg)

### [20261102-095335](../../20261102-095335/status.md) look into performance of this level

dwe2m1, same 2026-11-15 17:14 (0.00). `gotobug 20261102-095335`, setup `perf | 3`. Check: perf: about 4000 draw calls here, not 10000

[report](../../20261102-095335/screenshot.jpg) [baseline](../../20261102-095335/baseline.jpg) [now](../../20261102-095335/regress.jpg)

### [20261115-161600](../../20261115-161600/status.md) check if we can increase performance here

sstation, new baseline 2026-11-15 17:16. `gotobug 20261115-161600`. Check: frame time in the hall with the overlay: GPU about 1 ms less than with r_ao_half 0

[report](../../20261115-161600/screenshot.jpg) [baseline](../../20261115-161600/baseline.jpg) [now](../../20261115-161600/regress.jpg)

### [20261117-111050](../../20261117-111050/status.md) performance issues standing here

sevict, new baseline 2026-11-17 11:27. `gotobug 20261117-111050`, setup `"" | 3`. Check: r_perfoverlay 1: game total about 1.5 ms (was 10), sprites in r_hitchlog about 1 ms (was 8)

[report](../../20261117-111050/screenshot.jpg) [baseline](../../20261117-111050/baseline.jpg) [now](../../20261117-111050/regress.jpg)

### [20261117-113226](../../20261117-113226/status.md) another spot with performance

sevict, new baseline 2026-11-17 11:53. `gotobug 20261117-113226`, setup `"" | 3`. Check: perf at the spot: about 3600 draw calls (was 8700); movingmesh shows 420 and 1059 shadow still

[report](../../20261117-113226/screenshot.jpg) [baseline](../../20261117-113226/baseline.jpg) [now](../../20261117-113226/regress.jpg)

