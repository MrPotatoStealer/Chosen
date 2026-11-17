# Regression: sector-lighting

Build shading: sector shades, light effects and switches (World.SectorFx, the native world's b3d_sector data, BuildShading), sprite shading, overall brightness against NBlood.

19 bug(s), 19 in the last run. Rerun: `python pipeline/bugs.py regress sector-lighting` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-232559](../../20261015-232559/status.md) lightswitctes are not working, should turn on lights on interaction

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-232559`. Check: the light switch turns the rooms' lights on and off

[report](../../20261015-232559/screenshot.jpg) [NBlood](../../20261015-232559/nblood.jpg) [baseline](../../20261015-232559/baseline.jpg) [now](../../20261015-232559/regress.jpg)

### [20261016-084332](../../20261016-084332/status.md) incorrect light on secret that can be blasted open, I guess because of light changes with lightswitch

e1m1, same 2026-11-15 17:14 (0.04). `gotobug 20261016-084332`

[report](../../20261016-084332/screenshot.jpg) [NBlood](../../20261016-084332/nblood.jpg) [baseline](../../20261016-084332/baseline.jpg) [now](../../20261016-084332/regress.jpg)

### [20261018-154153](../../20261018-154153/status.md) sprites looks to dark

e1m2, same 2026-11-15 17:14 (0.02). `gotobug 20261018-154153`

[report](../../20261018-154153/screenshot.jpg) [NBlood](../../20261018-154153/nblood.jpg) [baseline](../../20261018-154153/baseline.jpg) [now](../../20261018-154153/regress.jpg)

### [20261018-162612](../../20261018-162612/status.md) sprite looks to dark

e1m2, same 2026-11-15 17:14 (0.01). `gotobug 20261018-162612`

[report](../../20261018-162612/screenshot.jpg) [NBlood](../../20261018-162612/nblood.jpg) [baseline](../../20261018-162612/baseline.jpg) [now](../../20261018-162612/regress.jpg)

### [20261018-162718](../../20261018-162718/status.md) sprite on button is too bright

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261018-162718`

[report](../../20261018-162718/screenshot.jpg) [NBlood](../../20261018-162718/nblood.jpg) [baseline](../../20261018-162718/baseline.jpg) [now](../../20261018-162718/regress.jpg)

### [20261018-212040](../../20261018-212040/status.md) should this hallway be this dark?

e1m2, same 2026-11-15 17:14 (0.06). `gotobug 20261018-212040`

[report](../../20261018-212040/screenshot.jpg) [NBlood](../../20261018-212040/nblood.jpg) [baseline](../../20261018-212040/baseline.jpg) [now](../../20261018-212040/regress.jpg)

### [20261018-220309](../../20261018-220309/status.md) door on this side should also follow the pulasating light

e1m2, same 2026-11-15 17:14 (0.08). `gotobug 20261018-220309`

[report](../../20261018-220309/screenshot.jpg) [NBlood](../../20261018-220309/nblood.jpg) [baseline](../../20261018-220309/baseline.jpg) [now](../../20261018-220309/regress.jpg)

### [20261019-091501](../../20261019-091501/status.md) sector is all dark

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261019-091501`

[report](../../20261019-091501/screenshot.jpg) [NBlood](../../20261019-091501/nblood.jpg) [baseline](../../20261019-091501/baseline.jpg) [now](../../20261019-091501/regress.jpg)

### [20261019-113150](../../20261019-113150/status.md) unable to destory lights, should make the sector darker

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261019-113150`. Check: break all three ceiling lights: the corridor goes dark (two are not enough)

[report](../../20261019-113150/screenshot.jpg) [NBlood](../../20261019-113150/nblood.jpg) [baseline](../../20261019-113150/baseline.jpg) [now](../../20261019-113150/regress.jpg)

### [20261019-130147](../../20261019-130147/status.md) there should be an increase in brightness on the ground when the lid opens

e1m1, same 2026-11-15 17:14 (0.06). `gotobug 20261019-130147`, setup `send s30 on | 4`. Check: the stairwell floor brightens as the coffin lid opens

[report](../../20261019-130147/screenshot.jpg) [NBlood](../../20261019-130147/nblood.jpg) [baseline](../../20261019-130147/baseline.jpg) [now](../../20261019-130147/regress.jpg)

### [20261019-130320](../../20261019-130320/status.md) game is overall to bright, or the contrast between light and dark areas is not very visible

e1m1, same 2026-11-15 17:14 (0.04). `gotobug 20261019-130320`

[report](../../20261019-130320/screenshot.jpg) [NBlood](../../20261019-130320/nblood.jpg) [baseline](../../20261019-130320/baseline.jpg) [now](../../20261019-130320/regress.jpg)

### [20261019-143701](../../20261019-143701/status.md) is this shelf to bright?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261019-143701`

[report](../../20261019-143701/screenshot.jpg) [NBlood](../../20261019-143701/nblood.jpg) [baseline](../../20261019-143701/baseline.jpg) [now](../../20261019-143701/regress.jpg)

### [20261019-143800](../../20261019-143800/status.md) ground to bright?

e1m2, same 2026-11-15 17:14 (0.11). `gotobug 20261019-143800`

[report](../../20261019-143800/screenshot.jpg) [NBlood](../../20261019-143800/nblood.jpg) [baseline](../../20261019-143800/baseline.jpg) [now](../../20261019-143800/regress.jpg)

### [20261019-144017](../../20261019-144017/status.md) this wall should not be flashing

e1m2, same 2026-11-15 17:14 (0.15). `gotobug 20261019-144017`

[report](../../20261019-144017/screenshot.jpg) [NBlood](../../20261019-144017/nblood.jpg) [baseline](../../20261019-144017/baseline.jpg) [now](../../20261019-144017/regress.jpg)

### [20261019-182027](../../20261019-182027/status.md) not sure where this dynamic ligt is coming from

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261019-182027`

[report](../../20261019-182027/screenshot.jpg) [NBlood](../../20261019-182027/nblood.jpg) [baseline](../../20261019-182027/baseline.jpg) [now](../../20261019-182027/regress.jpg)

### [20261019-210430](../../20261019-210430/status.md) this room over room is still not fixed,

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-210430`, setup `nomonsters | 0.2`

[report](../../20261019-210430/screenshot.jpg) [NBlood](../../20261019-210430/nblood.jpg) [baseline](../../20261019-210430/baseline.jpg) [now](../../20261019-210430/regress.jpg)

### [20261020-101910](../../20261020-101910/status.md) strange shading on this

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261020-101910`. Check: play <id> to the end: the turned shelf's side is evenly shaded

[report](../../20261020-101910/screenshot.jpg) [NBlood](../../20261020-101910/nblood.jpg) [baseline](../../20261020-101910/baseline.jpg) [now](../../20261020-101910/regress.jpg)

### [20261021-083036](../../20261021-083036/status.md) this does not look correct? Compare to NBlood

e1m8, same 2026-11-15 17:14 (0.15). `gotobug 20261021-083036`. Check: the corridor strips pinch in with slanted walls for the first seconds, then open; walls pulse with the light like floor and ceiling

[report](../../20261021-083036/screenshot.jpg) [NBlood](../../20261021-083036/nblood.jpg) [baseline](../../20261021-083036/baseline.jpg) [now](../../20261021-083036/regress.jpg)

### [20261029-171922](../../20261029-171922/status.md) fog is thicker or falloff is much lowe than Nblood

dwe3m1, same 2026-11-15 17:14 (0.02). `gotobug 20261029-171922`

[report](../../20261029-171922/screenshot.jpg) [NBlood](../../20261029-171922/nblood.jpg) [baseline](../../20261029-171922/baseline.jpg) [now](../../20261029-171922/regress.jpg)

