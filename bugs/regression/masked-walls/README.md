# Regression: masked-walls

Masked, translucent and one-way walls (cstat 16/32/128/512), palookup hints, double-sided faces.

17 bug(s), 17 in the last run. Rerun: `python pipeline/bugs.py regress masked-walls` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-154927](../../20261015-154927/status.md) this part of the hedge should look a bit different from the rest to hint that its a secret behind

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-154927`

[report](../../20261015-154927/screenshot.jpg) [NBlood](../../20261015-154927/nblood.jpg) [baseline](../../20261015-154927/baseline.jpg) [now](../../20261015-154927/regress.jpg)

### [20261015-161102](../../20261015-161102/status.md) window should be a bit transparent

e1m1, same 2026-11-15 17:14 (0.03). `gotobug 20261015-161102`

[report](../../20261015-161102/screenshot.jpg) [NBlood](../../20261015-161102/nblood.jpg) [baseline](../../20261015-161102/baseline.jpg) [now](../../20261015-161102/regress.jpg)

### [20261015-232646](../../20261015-232646/status.md) missing firsre texture hiding the inner part (secret) of the oven

e1m1, same 2026-11-15 17:14 (0.05). `gotobug 20261015-232646`

[report](../../20261015-232646/screenshot.jpg) [NBlood](../../20261015-232646/nblood.jpg) [baseline](../../20261015-232646/baseline.jpg) [now](../../20261015-232646/regress.jpg)

### [20261015-232829](../../20261015-232829/status.md) missing texture hiding the secret (cove) in the wall

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-232829`

[report](../../20261015-232829/screenshot.jpg) [NBlood](../../20261015-232829/nblood.jpg) [baseline](../../20261015-232829/baseline.jpg) [now](../../20261015-232829/regress.jpg)

### [20261016-084631](../../20261016-084631/status.md) can't shoot thru the window?

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261016-084631`. Check: break the window and shoot through it

[report](../../20261016-084631/screenshot.jpg) [NBlood](../../20261016-084631/nblood.jpg) [baseline](../../20261016-084631/baseline.jpg) [now](../../20261016-084631/regress.jpg)

### [20261016-084649](../../20261016-084649/status.md) also can't jump thru the window

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261016-084649`. Check: break the window and jump through it

[report](../../20261016-084649/screenshot.jpg) [NBlood](../../20261016-084649/nblood.jpg) [baseline](../../20261016-084649/baseline.jpg) [now](../../20261016-084649/regress.jpg)

### [20261019-091349](../../20261019-091349/status.md) sprites should be double sided, but can only be seen from one side. Or transparent wall that is here?

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261019-091349`

[report](../../20261019-091349/screenshot.jpg) [NBlood](../../20261019-091349/nblood.jpg) [baseline](../../20261019-091349/baseline.jpg) [now](../../20261019-091349/regress.jpg)

### [20261019-122949](../../20261019-122949/status.md) missing transparency on windows

e1m1, same 2026-11-15 17:14 (0.03). `gotobug 20261019-122949`

[report](../../20261019-122949/screenshot.jpg) [NBlood](../../20261019-122949/nblood.jpg) [baseline](../../20261019-122949/baseline.jpg) [now](../../20261019-122949/regress.jpg)

### [20261019-190325](../../20261019-190325/status.md) fog has collision

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-190325`. Check: walk through the fog

[report](../../20261019-190325/screenshot.jpg) [NBlood](../../20261019-190325/nblood.jpg) [baseline](../../20261019-190325/baseline.jpg) [now](../../20261019-190325/regress.jpg)

### [20261021-204849](../../20261021-204849/status.md) same as bug I reported before about missing sides, it looks like the gate here is missing a small piece of the wall

e2m2, same 2026-11-15 17:14 (0.05). `gotobug 20261021-204849`

[report](../../20261021-204849/screenshot.jpg) [NBlood](../../20261021-204849/nblood.jpg) [baseline](../../20261021-204849/baseline.jpg) [now](../../20261021-204849/regress.jpg)

### [20261024-100518](../../20261024-100518/status.md) I'm pretty sure the zombies here should come alive, lets check with NBlood

e1m4, same 2026-11-15 17:14 (0.04). `gotobug 20261024-100518`. Check: the axe zombies behind the broken wall wake and come through the hole

[report](../../20261024-100518/screenshot.jpg) [NBlood](../../20261024-100518/nblood.jpg) [baseline](../../20261024-100518/baseline.jpg) [now](../../20261024-100518/regress.jpg)

### [20261026-180824](../../20261026-180824/status.md) missing geo

e3m3, same 2026-11-15 17:14 (0.07). `gotobug 20261026-180824`

[report](../../20261026-180824/screenshot.jpg) [NBlood](../../20261026-180824/nblood.jpg) [baseline](../../20261026-180824/baseline.jpg) [now](../../20261026-180824/regress.jpg)

### [20261026-182348](../../20261026-182348/status.md) this looks incorrect

e3m5, same 2026-11-15 17:14 (0.14). `gotobug 20261026-182348`

[report](../../20261026-182348/screenshot.jpg) [NBlood](../../20261026-182348/nblood.jpg) [baseline](../../20261026-182348/baseline.jpg) [now](../../20261026-182348/regress.jpg)

### [20261026-182832](../../20261026-182832/status.md) missing geo

e3m6, same 2026-11-15 17:14 (0.20). `gotobug 20261026-182832`

[report](../../20261026-182832/screenshot.jpg) [NBlood](../../20261026-182832/nblood.jpg) [baseline](../../20261026-182832/baseline.jpg) [now](../../20261026-182832/regress.jpg)

### [20261027-202414](../../20261027-202414/status.md) hole in geo

e4m2, same 2026-11-15 17:14 (0.02). `gotobug 20261027-202414`. Check: the gate leaves start closed across the corridor (OFF), floor whole; send 117 on swings them open against the side walls

[report](../../20261027-202414/screenshot.jpg) [NBlood](../../20261027-202414/nblood.jpg) [baseline](../../20261027-202414/baseline.jpg) [now](../../20261027-202414/regress.jpg)

### [20261027-203112](../../20261027-203112/status.md) texture should be scrolling

e4m3, same 2026-11-15 17:14 (0.08). `gotobug 20261027-203112`. Check: the waterfall (masked wall 449/461) scrolls down

[report](../../20261027-203112/screenshot.jpg) [NBlood](../../20261027-203112/nblood.jpg) [baseline](../../20261027-203112/baseline.jpg) [now](../../20261027-203112/regress.jpg)

### [20261031-090743](../../20261031-090743/status.md) can't break window, and window is missing its collision

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261031-090743`. Check: shoot the train window: it shatters (shards, gone) and only then lets you through

[report](../../20261031-090743/screenshot.jpg) [NBlood](../../20261031-090743/nblood.jpg) [baseline](../../20261031-090743/baseline.jpg) [now](../../20261031-090743/regress.jpg)

