# Regression: dynamic-lights

DynamicLights.cs: fixture lights, their switches, shadows, copies through portals, performance.

15 bug(s), 15 in the last run. Rerun: `python pipeline/bugs.py regress dynamic-lights` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261019-162448](../../20261019-162448/status.md) light is not replicated through portal

e1m1, same 2026-11-17 12:07 (0.03). `gotobug 20261019-162448`

[report](../../20261019-162448/screenshot.jpg) [NBlood](../../20261019-162448/nblood.jpg) [baseline](../../20261019-162448/baseline.jpg) [now](../../20261019-162448/regress.jpg)

### [20261019-181609](../../20261019-181609/status.md) lights should not be active until the sprite itself is active, and destroyed if the sprite is destroyed

e1m1, same 2026-11-17 12:07 (0.06). `gotobug 20261019-181609`

[report](../../20261019-181609/screenshot.jpg) [NBlood](../../20261019-181609/nblood.jpg) [baseline](../../20261019-181609/baseline.jpg) [now](../../20261019-181609/regress.jpg)

### [20261019-181718](../../20261019-181718/status.md) no shadows even when shadow is turned on. I don't expect to see the light go thru the wall like this

e1m1, same 2026-11-17 12:07 (0.01). `gotobug 20261019-181718`

[report](../../20261019-181718/screenshot.jpg) [NBlood](../../20261019-181718/nblood.jpg) [baseline](../../20261019-181718/baseline.jpg) [now](../../20261019-181718/regress.jpg)

### [20261019-181857](../../20261019-181857/status.md) flickering looks strange, i like the idea of clickering but its not correct

e1m1, same 2026-11-17 12:07 (0.02). `gotobug 20261019-181857`. Check: the flicker is smooth, not a new random level every frame

[report](../../20261019-181857/screenshot.jpg) [NBlood](../../20261019-181857/nblood.jpg) [baseline](../../20261019-181857/baseline.jpg) [now](../../20261019-181857/regress.jpg)

### [20261019-181932](../../20261019-181932/status.md) dynamic lights is on even when the light switch is off

e1m1, same 2026-11-17 12:07 (0.01). `gotobug 20261019-181932`. Check: with the switch off the ceiling panels give no dynamic light

[report](../../20261019-181932/screenshot.jpg) [NBlood](../../20261019-181932/nblood.jpg) [baseline](../../20261019-181932/baseline.jpg) [now](../../20261019-181932/regress.jpg)

### [20261019-182027](../../20261019-182027/status.md) not sure where this dynamic ligt is coming from

e1m1, same 2026-11-17 12:07 (0.00). `gotobug 20261019-182027`

[report](../../20261019-182027/screenshot.jpg) [NBlood](../../20261019-182027/nblood.jpg) [baseline](../../20261019-182027/baseline.jpg) [now](../../20261019-182027/regress.jpg)

### [20261019-184126](../../20261019-184126/status.md) performance issues due to dynamic lights even when I cant see any?

e1m2, same 2026-11-17 12:07 (0.01). `gotobug 20261019-184126`. Check: perf with dynamic lights and shadows on

[report](../../20261019-184126/screenshot.jpg) [NBlood](../../20261019-184126/nblood.jpg) [baseline](../../20261019-184126/baseline.jpg) [now](../../20261019-184126/regress.jpg)

### [20261025-102657](../../20261025-102657/status.md) dynamic lights in room in room?

e2m5, same 2026-11-17 12:07 (0.01). `gotobug 20261025-102657`

[report](../../20261025-102657/screenshot.jpg) [NBlood](../../20261025-102657/nblood.jpg) [baseline](../../20261025-102657/baseline.jpg) [now](../../20261025-102657/regress.jpg)

### [20261029-105717](../../20261029-105717/status.md) incorrect brightness on sprites in the ceiling?

dwe3m1, same 2026-11-17 12:07 (0.01). `gotobug 20261029-105717`

[report](../../20261029-105717/screenshot.jpg) [NBlood](../../20261029-105717/nblood.jpg) [baseline](../../20261029-105717/baseline.jpg) [now](../../20261029-105717/regress.jpg)

### [20261031-220422](../../20261031-220422/status.md) can we fix so lights behind wallks are not shining through. Double sided geo on shadows maybe?

e1m1, same 2026-11-17 12:07 (0.00). `gotobug 20261031-220422`. Check: with shadows on: no torch light through the wall from the next room

[report](../../20261031-220422/screenshot.jpg) [baseline](../../20261031-220422/baseline.jpg) [now](../../20261031-220422/regress.jpg)

### [20261101-151717](../../20261101-151717/status.md) light shining through wall is back

e1m1, same 2026-11-17 12:07 (0.01). `gotobug 20261101-151717`

[report](../../20261101-151717/screenshot.jpg) [baseline](../../20261101-151717/baseline.jpg) [now](../../20261101-151717/regress.jpg)

### [20261101-170716](../../20261101-170716/status.md) look into if there is a fix for the bleeding of moonlight?

e1m5, same 2026-11-17 12:07 (0.00). `gotobug 20261101-170716`, setup `r_moonlight 1 | 3`. Check: no blue edges along the walls under the ceiling

[report](../../20261101-170716/screenshot.jpg) [baseline](../../20261101-170716/baseline.jpg) [now](../../20261101-170716/regress.jpg)

### [20261101-211612](../../20261101-211612/status.md) the dynamic light coming from candles looks a bit off

mrw2m1, same 2026-11-17 12:07 (0.00). `gotobug 20261101-211612`

[report](../../20261101-211612/screenshot.jpg) [NBlood](../../20261101-211612/nblood.jpg) [baseline](../../20261101-211612/baseline.jpg) [now](../../20261101-211612/regress.jpg)

### [20261101-211805](../../20261101-211805/status.md) same here dynamic light is a bit off from the actual lantern

e1m1, same 2026-11-17 12:07 (0.03). `gotobug 20261101-211805`

[report](../../20261101-211805/screenshot.jpg) [NBlood](../../20261101-211805/nblood.jpg) [baseline](../../20261101-211805/baseline.jpg) [now](../../20261101-211805/regress.jpg)

### [20261102-083959](../../20261102-083959/status.md) lamps are only half lit, half bright and half dull

e1m1, same 2026-11-17 12:07 (0.00). `gotobug 20261102-083959`

[report](../../20261102-083959/screenshot.jpg) [NBlood](../../20261102-083959/nblood.jpg) [baseline](../../20261102-083959/baseline.jpg) [now](../../20261102-083959/regress.jpg)

