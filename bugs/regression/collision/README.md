# Regression: collision

Player/actor collision (Sim/Collision): blocking walls and sprites, ledges, link crossing. The screenshot only finds the spot: retest by playing there (or bugs.py replay).

39 bug(s), 39 in the last run. Rerun: `python pipeline/bugs.py regress collision` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-120116](../../20261015-120116/status.md) no collision on the round sliding door, interaction is not working on it as well.

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-120116`. Check: the round door has collision and can be used

[report](../../20261015-120116/screenshot.jpg) [NBlood](../../20261015-120116/nblood.jpg) [baseline](../../20261015-120116/baseline.jpg) [now](../../20261015-120116/regress.jpg)

### [20261015-161045](../../20261015-161045/status.md) gates are closed but has no collision

e1m1, same 2026-11-15 17:14 (0.03). `gotobug 20261015-161045`. Check: the closed gates block

[report](../../20261015-161045/screenshot.jpg) [NBlood](../../20261015-161045/nblood.jpg) [baseline](../../20261015-161045/baseline.jpg) [now](../../20261015-161045/regress.jpg)

### [20261015-230458](../../20261015-230458/status.md) strange collision here, I can crouch and walk thru the gap

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-230458`. Check: the vent openings block when crouching

[report](../../20261015-230458/screenshot.jpg) [NBlood](../../20261015-230458/nblood.jpg) [baseline](../../20261015-230458/baseline.jpg) [now](../../20261015-230458/regress.jpg)

### [20261016-084017](../../20261016-084017/status.md) jumping next to the gate (sprite) here and you are teleported to other side of the sprite in the air for a few frames

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261016-084017`. Check: jump beside the gate: the view stays on this side

[report](../../20261016-084017/screenshot.jpg) [NBlood](../../20261016-084017/nblood.jpg) [baseline](../../20261016-084017/baseline.jpg) [now](../../20261016-084017/regress.jpg)

### [20261016-084440](../../20261016-084440/status.md) standing in this area should not be possible, should remain crouched?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-084440`. Check: releasing crouch under the low ceiling keeps the eye under it

[report](../../20261016-084440/screenshot.jpg) [NBlood](../../20261016-084440/nblood.jpg) [baseline](../../20261016-084440/baseline.jpg) [now](../../20261016-084440/regress.jpg)

### [20261016-084631](../../20261016-084631/status.md) can't shoot thru the window?

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261016-084631`. Check: break the window and shoot through it

[report](../../20261016-084631/screenshot.jpg) [NBlood](../../20261016-084631/nblood.jpg) [baseline](../../20261016-084631/baseline.jpg) [now](../../20261016-084631/regress.jpg)

### [20261016-084649](../../20261016-084649/status.md) also can't jump thru the window

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261016-084649`. Check: break the window and jump through it

[report](../../20261016-084649/screenshot.jpg) [NBlood](../../20261016-084649/nblood.jpg) [baseline](../../20261016-084649/baseline.jpg) [now](../../20261016-084649/regress.jpg)

### [20261016-095604](../../20261016-095604/status.md) player pushing against the rotatign door collider prevents it from moving

e1m1, same 2026-11-15 17:14 (0.19). `gotobug 20261016-095604`. Check: stand in the rotating door's way: it still turns

[report](../../20261016-095604/screenshot.jpg) [NBlood](../../20261016-095604/nblood.jpg) [baseline](../../20261016-095604/baseline.jpg) [now](../../20261016-095604/regress.jpg)

### [20261016-100058](../../20261016-100058/status.md) vase requires multiple hits to destroy with pitchfork, incorrect damage or hp?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-100058`. Check: the vase breaks in one pitchfork hit

[report](../../20261016-100058/screenshot.jpg) [NBlood](../../20261016-100058/nblood.jpg) [baseline](../../20261016-100058/baseline.jpg) [now](../../20261016-100058/regress.jpg)

### [20261016-100306](../../20261016-100306/status.md) can jump up this ledge, there is almost like a ledge grab happening pushing the player up. I guess this is because player is using a capsule collider where original is just a position?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-100306`. Check: jumping at the ledge does not pull the player up

[report](../../20261016-100306/screenshot.jpg) [NBlood](../../20261016-100306/nblood.jpg) [baseline](../../20261016-100306/baseline.jpg) [now](../../20261016-100306/regress.jpg)

### [20261016-154112](../../20261016-154112/status.md) you can stand on the small collision here, in NBlood it should not be possible and you fall to ground

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-154112`. Check: no standing on the alcove ledge behind the outdoor wall

[report](../../20261016-154112/screenshot.jpg) [NBlood](../../20261016-154112/nblood.jpg) [baseline](../../20261016-154112/baseline.jpg) [now](../../20261016-154112/regress.jpg)

### [20261016-184910](../../20261016-184910/status.md) OutOf Bounds collision jump

e1m1, same 2026-11-15 17:14 (0.08). `gotobug 20261016-184910`, setup `send s30 on | 4`. Check: fall back through the coffin portal near its edge: no out of bounds

[report](../../20261016-184910/screenshot.jpg) [NBlood](../../20261016-184910/nblood.jpg) [baseline](../../20261016-184910/baseline.jpg) [now](../../20261016-184910/regress.jpg)

### [20261018-154100](../../20261018-154100/status.md) got stuck in collision from firing and being pushed into the wall

e1m2, same 2026-11-15 17:14 (0.03). `gotobug 20261018-154100`. Check: pushed into the turntable car wall: no getting stuck

[report](../../20261018-154100/screenshot.jpg) [NBlood](../../20261018-154100/nblood.jpg) [baseline](../../20261018-154100/baseline.jpg) [now](../../20261018-154100/regress.jpg)

### [20261018-154403](../../20261018-154403/status.md) I opened the secret but the collision is preventing me from getting the healthpack

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261018-154403`. Check: open the secret: nothing blocks the health pack

[report](../../20261018-154403/screenshot.jpg) [NBlood](../../20261018-154403/nblood.jpg) [baseline](../../20261018-154403/baseline.jpg) [now](../../20261018-154403/regress.jpg)

### [20261018-154642](../../20261018-154642/status.md) collision still exist after blowing up the ground here

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261018-154642`. Check: blow up the ground: no invisible floor left

[report](../../20261018-154642/screenshot.jpg) [NBlood](../../20261018-154642/nblood.jpg) [baseline](../../20261018-154642/baseline.jpg) [now](../../20261018-154642/regress.jpg)

### [20261019-122623](../../20261019-122623/status.md) standing and jumping on grave puts camers out of bounds?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261019-122623`. Check: jumping on the grave keeps the head under the sky

[report](../../20261019-122623/screenshot.jpg) [NBlood](../../20261019-122623/nblood.jpg) [baseline](../../20261019-122623/baseline.jpg) [now](../../20261019-122623/regress.jpg)

### [20261019-190325](../../20261019-190325/status.md) fog has collision

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-190325`. Check: walk through the fog

[report](../../20261019-190325/screenshot.jpg) [NBlood](../../20261019-190325/nblood.jpg) [baseline](../../20261019-190325/baseline.jpg) [now](../../20261019-190325/regress.jpg)

### [20261020-231412](../../20261020-231412/status.md) killed the boss but level is not finished

e1m7, same 2026-11-15 17:14 (0.01). `gotobug 20261020-231412`. Check: kill the gargoyles in the sky-lit courtyard: none flies out over the walls; after all of them die the exit opens

[report](../../20261020-231412/screenshot.jpg) [baseline](../../20261020-231412/baseline.jpg) [now](../../20261020-231412/regress.jpg)

### [20261021-083322](../../20261021-083322/status.md) should be able to shoot through this window, but prevent player collision

e1m4, same 2026-11-15 17:14 (0.00). `gotobug 20261021-083322`. Check: shoot the window grate: bullets go through, the player can't walk through

[report](../../20261021-083322/screenshot.jpg) [baseline](../../20261021-083322/baseline.jpg) [now](../../20261021-083322/regress.jpg)

### [20261023-171745](../../20261023-171745/status.md) ignore previous bug report issues is with collision on the train I cant get on it

e1m2, same 2026-11-15 17:14 (0.03). `gotobug 20261023-171745`, setup `send 111 | 16`. Check: step from the platform onto the train car and walk across it

[report](../../20261023-171745/screenshot.jpg) [NBlood](../../20261023-171745/nblood.jpg) [baseline](../../20261023-171745/baseline.jpg) [now](../../20261023-171745/regress.jpg)

### [20261024-100518](../../20261024-100518/status.md) I'm pretty sure the zombies here should come alive, lets check with NBlood

e1m4, same 2026-11-15 17:14 (0.04). `gotobug 20261024-100518`. Check: the axe zombies behind the broken wall wake and come through the hole

[report](../../20261024-100518/screenshot.jpg) [NBlood](../../20261024-100518/nblood.jpg) [baseline](../../20261024-100518/baseline.jpg) [now](../../20261024-100518/regress.jpg)

### [20261024-193401](../../20261024-193401/status.md) this room is not correct, collision in the way so can't even enter

e1m5, same 2026-11-15 17:14 (0.02). `gotobug 20261024-193401`, setup `send s210 on | 3`. Check: walk through the double door into the hall: floor, ceiling and pillars as in NBlood, nothing blocking

[report](../../20261024-193401/screenshot.jpg) [NBlood](../../20261024-193401/nblood.jpg) [baseline](../../20261024-193401/baseline.jpg) [now](../../20261024-193401/regress.jpg)

### [20261025-105901](../../20261025-105901/status.md) invisible collision here

e2m5, same 2026-11-15 17:14 (0.02). `gotobug 20261025-105901`. Check: walk east from the spot at y 935 (and 952): the player passes x -428 into the alcove instead of stopping

[report](../../20261025-105901/screenshot.jpg) [NBlood](../../20261025-105901/nblood.jpg) [baseline](../../20261025-105901/baseline.jpg) [now](../../20261025-105901/regress.jpg)

### [20261025-160648](../../20261025-160648/status.md) teleport not working?

e3m5, same 2026-11-15 17:14 (0.19). `gotobug 20261025-160648`, setup `warp 545 32 -96 0 0; walk 40 | 3`. Check: walking onto the teleporter takes the player to the secret room (invulnerability)

[report](../../20261025-160648/screenshot.jpg) [NBlood](../../20261025-160648/nblood.jpg) [baseline](../../20261025-160648/baseline.jpg) [now](../../20261025-160648/regress.jpg)

### [20261026-130808](../../20261026-130808/status.md) invisisble collision

e3m1, same 2026-11-15 17:14 (0.01). `gotobug 20261026-130808`, setup `nomonsters | 0`. Check: walk from the spot towards the bar's west wall: no invisible wall

[report](../../20261026-130808/screenshot.jpg) [NBlood](../../20261026-130808/nblood.jpg) [baseline](../../20261026-130808/baseline.jpg) [now](../../20261026-130808/regress.jpg)

### [20261026-141026](../../20261026-141026/status.md) invisible collision after raising floor, can't progress

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-141026`, setup `nomonsters; send 117 on | 5`. Check: walk up the raised crates from the report spot (south)

[report](../../20261026-141026/screenshot.jpg) [NBlood](../../20261026-141026/nblood.jpg) [baseline](../../20261026-141026/baseline.jpg) [now](../../20261026-141026/regress.jpg)

### [20261026-143245](../../20261026-143245/status.md) fell of of world

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-143245`, setup `nomonsters; send 117 on | 6`. Check: from room 141 (2134 -1137, floor -56) walk east under the raised crates: blocked, no fall

[report](../../20261026-143245/screenshot.jpg) [NBlood](../../20261026-143245/nblood.jpg) [baseline](../../20261026-143245/baseline.jpg) [now](../../20261026-143245/regress.jpg)

### [20261027-092415](../../20261027-092415/status.md) collision on moving platform here stopped working

e3m5, same 2026-11-15 17:14 (0.00). `gotobug 20261027-092415`. Check: jump east from the ledge onto the sliding platform (e3m5-platform): you land on it, not through the shaft's link

[report](../../20261027-092415/screenshot.jpg) [baseline](../../20261027-092415/baseline.jpg) [now](../../20261027-092415/regress.jpg)

### [20261027-092928](../../20261027-092928/status.md) no collision on door

e1m4, same 2026-11-15 17:14 (0.01). `gotobug 20261027-092928`. Check: walk north into the swing door from the south (e1m4-door): it blocks, as the brushes do

[report](../../20261027-092928/screenshot.jpg) [baseline](../../20261027-092928/baseline.jpg) [now](../../20261027-092928/regress.jpg)

### [20261027-093224](../../20261027-093224/status.md) dont rotate with carousle

e1m4, same 2026-11-15 17:14 (0.02). `gotobug 20261027-093224`. Check: walk from room 352 onto the carousel (e1m4-carousel): standing still, it carries you round, feet at -128

[report](../../20261027-093224/screenshot.jpg) [baseline](../../20261027-093224/baseline.jpg) [now](../../20261027-093224/regress.jpg)

### [20261027-202000](../../20261027-202000/status.md) broken window but collision is still there

e4m1, same 2026-11-15 17:14 (0.12). `gotobug 20261027-202000`. Check: break the window: then you can walk through (Build's collision clears its flags)

[report](../../20261027-202000/screenshot.jpg) [baseline](../../20261027-202000/baseline.jpg) [now](../../20261027-202000/regress.jpg)

### [20261027-202333](../../20261027-202333/status.md) door missing collision

e4m2, same 2026-11-15 17:14 (0.00). `gotobug 20261027-202333`. Check: walk into the shut sliding door: it blocks (phys_sectors 1 and 0); send 104 on, then it lets you through

[report](../../20261027-202333/screenshot.jpg) [NBlood](../../20261027-202333/nblood.jpg) [baseline](../../20261027-202333/baseline.jpg) [now](../../20261027-202333/regress.jpg)

### [20261028-085657](../../20261028-085657/status.md) collision on falling teleport you up on the platform, in NBlood you hit the platform side and fall down into the lave. Here you somehow teleport up on the platform instead

e3m5, same 2026-11-15 17:14 (0.00). `gotobug 20261028-085657`. Check: fall through the hole in the floor in 211 beside the turning block: you land in the lava below (210/335), never thrown back up onto the block

[report](../../20261028-085657/screenshot.jpg) [NBlood](../../20261028-085657/nblood.jpg) [baseline](../../20261028-085657/baseline.jpg) [now](../../20261028-085657/regress.jpg)

### [20261030-101950](../../20261030-101950/status.md) crouching in boat made me push into water

dwe3m1, same 2026-11-15 17:14 (0.02). `gotobug 20261030-101950`, setup `nomonsters | 0`. Check: stand in the boat and crouch: you stay in it, not under the river

[report](../../20261030-101950/screenshot.jpg) [baseline](../../20261030-101950/baseline.jpg) [now](../../20261030-101950/regress.jpg)

### [20261030-113748](../../20261030-113748/status.md) have to spam click to get boat going?

dwe3m1, same 2026-11-15 17:14 (0.02). `gotobug 20261030-113748`, setup `nomonsters | 0`. Check: crouch in the boat facing the lever panel and press use once: the boat sets off

[report](../../20261030-113748/screenshot.jpg) [baseline](../../20261030-113748/baseline.jpg) [now](../../20261030-113748/regress.jpg)

### [20261031-090743](../../20261031-090743/status.md) can't break window, and window is missing its collision

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261031-090743`. Check: shoot the train window: it shatters (shards, gone) and only then lets you through

[report](../../20261031-090743/screenshot.jpg) [NBlood](../../20261031-090743/nblood.jpg) [baseline](../../20261031-090743/baseline.jpg) [now](../../20261031-090743/regress.jpg)

### [20261101-191705](../../20261101-191705/status.md) starting e1m6 and you die instantly, think you are spawning in the wrong sectors since its a room in room

e1m6, same 2026-11-15 17:14 (0.00). `gotobug 20261101-191705`. Check: start E1M6: in the tunnel, not dropped among cultists; level start, not this report's spot

[report](../../20261101-191705/screenshot.jpg) [NBlood](../../20261101-191705/nblood.jpg) [baseline](../../20261101-191705/baseline.jpg) [now](../../20261101-191705/regress.jpg)

### [20261101-221212](../../20261101-221212/status.md) not following the iceberg, check with NBlood if we should be when standing on it? Also didn't fall down once the iceberg was gone from under me.

e2m1, same 2026-11-15 17:14 (0.00). `gotobug 20261101-221212`, setup `b3d_fixblood 1 | 3`. Check: from the spot the player falls straight into the water (b3d_fixblood), not left standing in the air where the iceberg was

[report](../../20261101-221212/screenshot.jpg) [baseline](../../20261101-221212/baseline.jpg) [now](../../20261101-221212/regress.jpg)

### [20261102-090029](../../20261102-090029/status.md) napalm going through enemies

dwe3m1, same 2026-11-15 17:14 (0.00). `gotobug 20261102-090029`

[report](../../20261102-090029/screenshot.jpg) [NBlood](../../20261102-090029/nblood.jpg) [baseline](../../20261102-090029/baseline.jpg) [now](../../20261102-090029/regress.jpg)

