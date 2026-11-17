# Regression: carried-sprites

Sprites that move with sectors (cstat 8192/16384, moveZPart/zPart, turntables) and sprites that wait inside another sector.

7 bug(s), 7 in the last run. Rerun: `python pipeline/bugs.py regress carried-sprites` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-154952](../../20261015-154952/status.md) gates are open from the start

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261015-154952`

[report](../../20261015-154952/screenshot.jpg) [NBlood](../../20261015-154952/nblood.jpg) [baseline](../../20261015-154952/baseline.jpg) [now](../../20261015-154952/regress.jpg)

### [20261015-230345](../../20261015-230345/status.md) blowing a whole in the wall here makes the floor go up?

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-230345`. Check: blow the wall: the floor stays, only the fire sprites rise

[report](../../20261015-230345/screenshot.jpg) [NBlood](../../20261015-230345/nblood.jpg) [baseline](../../20261015-230345/baseline.jpg) [now](../../20261015-230345/regress.jpg)

### [20261015-232326](../../20261015-232326/status.md) fire should not be visible before blowing up the funeral home

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261015-232326`

[report](../../20261015-232326/screenshot.jpg) [NBlood](../../20261015-232326/nblood.jpg) [baseline](../../20261015-232326/baseline.jpg) [now](../../20261015-232326/regress.jpg)

### [20261018-154121](../../20261018-154121/status.md) sprite is not rotating with sector

e1m2, same 2026-11-15 17:14 (0.02). `gotobug 20261018-154121`. Check: turntable: sprites rotate with it, also after destroying some

[report](../../20261018-154121/screenshot.jpg) [NBlood](../../20261018-154121/nblood.jpg) [baseline](../../20261018-154121/baseline.jpg) [now](../../20261018-154121/regress.jpg)

### [20261018-221903](../../20261018-221903/status.md) dynamite is not rotating with sector, also one dynamite fell thru the ground

e1m2, same 2026-11-15 17:14 (0.01). `gotobug 20261018-221903`. Check: turntable: dynamite rotates with it, none falls through

[report](../../20261018-221903/screenshot.jpg) [NBlood](../../20261018-221903/nblood.jpg) [baseline](../../20261018-221903/baseline.jpg) [now](../../20261018-221903/regress.jpg)

### [20261019-091425](../../20261019-091425/status.md) another portal bug where it does not align correctly

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261019-091425`

[report](../../20261019-091425/screenshot.jpg) [NBlood](../../20261019-091425/nblood.jpg) [baseline](../../20261019-091425/baseline.jpg) [now](../../20261019-091425/regress.jpg)

### [20261031-092746](../../20261031-092746/status.md) human at start got stuck, it should collide with the train

e1m3, same 2026-11-15 17:14 (0.04). `gotobug 20261031-092746`. Check: the human on the tracks comes down to the train and bursts there about 5 s in, then the next two props in turn

[report](../../20261031-092746/screenshot.jpg) [NBlood](../../20261031-092746/nblood.jpg) [baseline](../../20261031-092746/baseline.jpg) [now](../../20261031-092746/regress.jpg)

