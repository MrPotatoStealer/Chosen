# Regression: water

Water and other liquids: entering and leaving through water links, underwater sectors (World.Physics CheckLink, InUnderwaterSector), swimming, the screen palettes (ViewPalette). The screenshot only finds the spot: retest by playing the recording (bugs.py replay) and looking at the end.

10 bug(s), 10 in the last run. Rerun: `python pipeline/bugs.py regress water` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261020-105925](../../20261020-105925/status.md) missign underwater filter?

e1m4, same 2026-11-15 17:14 (0.08). `gotobug 20261020-105925`. Check: play <id>: at the end 'where' shows posture 1 medium 1 underwater True; the screen is in the water palette; jump/crouch swim up/down

[report](../../20261020-105925/screenshot.jpg) [NBlood](../../20261020-105925/nblood.jpg) [baseline](../../20261020-105925/baseline.jpg) [now](../../20261020-105925/regress.jpg)

### [20261020-105949](../../20261020-105949/status.md) how do I swim up and down?

e1m4, same 2026-11-15 17:14 (0.09). `gotobug 20261020-105949`. Check: play <id>: at the end 'where' shows posture 1 medium 1 underwater True; the screen is in the water palette; jump/crouch swim up/down

[report](../../20261020-105949/screenshot.jpg) [NBlood](../../20261020-105949/nblood.jpg) [baseline](../../20261020-105949/baseline.jpg) [now](../../20261020-105949/regress.jpg)

### [20261022-083332](../../20261022-083332/status.md) missing swimming volume here

e2m9, same 2026-11-15 17:14 (0.06). `gotobug 20261022-083332`. Check: the view is under water (blue tint): the player swims here

[report](../../20261022-083332/screenshot.jpg) [NBlood](../../20261022-083332/nblood.jpg) [baseline](../../20261022-083332/baseline.jpg) [now](../../20261022-083332/regress.jpg)

### [20261026-180722](../../20261026-180722/status.md) some glitches here going down into the water sometimes, like 1 frame the wrong stuff is rendered

e3m3, same 2026-11-15 17:14 (0.00). `gotobug 20261026-180722`. Check: walk along the flooded stairs with the eye just over the surface: the room above never drops out for a frame

[report](../../20261026-180722/screenshot.jpg) [NBlood](../../20261026-180722/nblood.jpg) [baseline](../../20261026-180722/baseline.jpg) [now](../../20261026-180722/regress.jpg)

### [20261027-203545](../../20261027-203545/status.md) small section here where it should be water

e4m4, same 2026-11-15 17:14 (0.06). `gotobug 20261027-203545`. Check: swim up from 117 through its ceiling into 118: the view stays underwater all the way

[report](../../20261027-203545/screenshot.jpg) [NBlood](../../20261027-203545/nblood.jpg) [baseline](../../20261027-203545/baseline.jpg) [now](../../20261027-203545/regress.jpg)

### [20261028-103255](../../20261028-103255/status.md) here is the part where flickering happening

e3m3, same 2026-11-15 17:14 (0.01). `gotobug 20261028-103255`. Check: crouch-swim on the top flooded step (e3m3-stairs, hold crouch): the room above never drops out

[report](../../20261028-103255/screenshot.jpg) [baseline](../../20261028-103255/baseline.jpg) [now](../../20261028-103255/regress.jpg)

### [20261030-101950](../../20261030-101950/status.md) crouching in boat made me push into water

dwe3m1, same 2026-11-15 17:14 (0.02). `gotobug 20261030-101950`, setup `nomonsters | 0`. Check: stand in the boat and crouch: you stay in it, not under the river

[report](../../20261030-101950/screenshot.jpg) [baseline](../../20261030-101950/baseline.jpg) [now](../../20261030-101950/regress.jpg)

### [20261031-084013](../../20261031-084013/status.md) transparent water bug, missing the portal renderer for the other side

e2m6, same 2026-11-15 17:14 (0.00). `gotobug 20261031-084013`, setup `send s1 on | 5`. Check: the slid-open ice floe's water shows the room under it

[report](../../20261031-084013/screenshot.jpg) [NBlood](../../20261031-084013/nblood.jpg) [baseline](../../20261031-084013/baseline.jpg) [now](../../20261031-084013/regress.jpg)

### [20261031-113433](../../20261031-113433/status.md) gaps in water geo when boat is moving

dwe3m1, same 2026-11-15 17:14 (0.00). `gotobug 20261031-113433`. Check: ride the boat and open doors: no long thin lines across the water or the floor beside them while they move

[report](../../20261031-113433/screenshot.jpg) [baseline](../../20261031-113433/baseline.jpg) [now](../../20261031-113433/regress.jpg)

### [20261031-172211](../../20261031-172211/status.md) napalm sprite get stuck in portal after firing

e2m1, same 2026-11-15 17:14 (0.00). `gotobug 20261031-172211`. Check: fire napalm into the water: no ball left hanging on the surface

[report](../../20261031-172211/screenshot.jpg) [baseline](../../20261031-172211/baseline.jpg) [now](../../20261031-172211/regress.jpg)

