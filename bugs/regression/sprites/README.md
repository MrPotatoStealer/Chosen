# Regression: sprites

SpriteRenderer and voxels: offsets, one-sided and wall sprites, translucency, procedural fire, flips.

27 bug(s), 27 in the last run. Rerun: `python pipeline/bugs.py regress sprites` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-161045](../../20261015-161045/status.md) gates are closed but has no collision

e1m1, same 2026-11-17 12:06 (0.03). `gotobug 20261015-161045`. Check: the closed gates block

[report](../../20261015-161045/screenshot.jpg) [NBlood](../../20261015-161045/nblood.jpg) [baseline](../../20261015-161045/baseline.jpg) [now](../../20261015-161045/regress.jpg)

### [20261016-095536](../../20261016-095536/status.md) sprites missing a bit of transparency

e1m1, same 2026-11-17 12:06 (0.01). `gotobug 20261016-095536`

[report](../../20261016-095536/screenshot.jpg) [NBlood](../../20261016-095536/nblood.jpg) [baseline](../../20261016-095536/baseline.jpg) [now](../../20261016-095536/regress.jpg)

### [20261016-100021](../../20261016-100021/status.md) sprites are not animated, also this fire sprite should be brighter

e1m1, same 2026-11-17 12:06 (0.04). `gotobug 20261016-100021`

[report](../../20261016-100021/screenshot.jpg) [NBlood](../../20261016-100021/nblood.jpg) [baseline](../../20261016-100021/baseline.jpg) [now](../../20261016-100021/regress.jpg)

### [20261016-100058](../../20261016-100058/status.md) vase requires multiple hits to destroy with pitchfork, incorrect damage or hp?

e1m1, same 2026-11-17 12:06 (0.00). `gotobug 20261016-100058`. Check: the vase breaks in one pitchfork hit

[report](../../20261016-100058/screenshot.jpg) [NBlood](../../20261016-100058/nblood.jpg) [baseline](../../20261016-100058/baseline.jpg) [now](../../20261016-100058/regress.jpg)

### [20261016-110712](../../20261016-110712/status.md) burning tree animations make it move sideways during some of it frames. Like center is shifted

e1m1, same 2026-11-17 12:06 (0.02). `gotobug 20261016-110712`

[report](../../20261016-110712/screenshot.jpg) [NBlood](../../20261016-110712/nblood.jpg) [baseline](../../20261016-110712/baseline.jpg) [now](../../20261016-110712/regress.jpg)

### [20261016-111009](../../20261016-111009/status.md) sprite is in wrong location when seen thru a portal, you can see other sprites liek spiderwebs also shifting location

e1m1, same 2026-11-17 12:06 (0.06). `gotobug 20261016-111009`, setup `send s30 on | 4`

[report](../../20261016-111009/screenshot.jpg) [NBlood](../../20261016-111009/nblood.jpg) [baseline](../../20261016-111009/baseline.jpg) [now](../../20261016-111009/regress.jpg)

### [20261016-153928](../../20261016-153928/status.md) strange shading on the curtains

e1m1, same 2026-11-17 12:06 (0.00). `gotobug 20261016-153928`

[report](../../20261016-153928/screenshot.jpg) [NBlood](../../20261016-153928/nblood.jpg) [baseline](../../20261016-153928/baseline.jpg) [now](../../20261016-153928/regress.jpg)

### [20261018-213410](../../20261018-213410/status.md) voxel version of the fence/gate is upside down

e1m1, same 2026-11-17 12:06 (0.02). `gotobug 20261018-213410`

[report](../../20261018-213410/screenshot.jpg) [NBlood](../../20261018-213410/nblood.jpg) [baseline](../../20261018-213410/baseline.jpg) [now](../../20261018-213410/regress.jpg)

### [20261019-091242](../../20261019-091242/status.md) voxel/sprites are not being rendered

e1m1, same 2026-11-17 12:06 (0.00). `gotobug 20261019-091242`

[report](../../20261019-091242/screenshot.jpg) [NBlood](../../20261019-091242/nblood.jpg) [baseline](../../20261019-091242/baseline.jpg) [now](../../20261019-091242/regress.jpg)

### [20261019-104046](../../20261019-104046/status.md) missing sprite when viewed thru portal

e1m1, same 2026-11-17 12:06 (0.06). `gotobug 20261019-104046`, setup `send s30 on | 4`

[report](../../20261019-104046/screenshot.jpg) [NBlood](../../20261019-104046/nblood.jpg) [baseline](../../20261019-104046/baseline.jpg) [now](../../20261019-104046/regress.jpg)

### [20261019-122850](../../20261019-122850/status.md) fire is not animating, prerender textures like in darkplaces or convert it to an actual shader. Check NBlood how it's originally done

e1m1, same 2026-11-17 12:06 (0.03). `gotobug 20261019-122850`

[report](../../20261019-122850/screenshot.jpg) [NBlood](../../20261019-122850/nblood.jpg) [baseline](../../20261019-122850/baseline.jpg) [now](../../20261019-122850/regress.jpg)

### [20261019-122928](../../20261019-122928/status.md) sprites appearing and disappearing based on distance

e1m1, same 2026-11-17 12:06 (0.05). `gotobug 20261019-122928`

[report](../../20261019-122928/screenshot.jpg) [NBlood](../../20261019-122928/nblood.jpg) [baseline](../../20261019-122928/baseline.jpg) [now](../../20261019-122928/regress.jpg)

### [20261019-162631](../../20261019-162631/status.md) sprite shift offset when rendering through portal

e1m1, same 2026-11-17 12:06 (0.03). `gotobug 20261019-162631`

[report](../../20261019-162631/screenshot.jpg) [NBlood](../../20261019-162631/nblood.jpg) [baseline](../../20261019-162631/baseline.jpg) [now](../../20261019-162631/regress.jpg)

### [20261023-224613](../../20261023-224613/status.md) why is there two keys here?

e1m8, same 2026-11-17 12:06 (0.09). `gotobug 20261023-224613`, setup `nomonsters | 2`. Check: one skull key and one chest on the walkway

[report](../../20261023-224613/screenshot.jpg) [NBlood](../../20261023-224613/nblood.jpg) [baseline](../../20261023-224613/baseline.jpg) [now](../../20261023-224613/regress.jpg)

### [20261024-182730](../../20261024-182730/status.md) slight gap

e2m3, same 2026-11-17 12:06 (0.00). `gotobug 20261024-182730`, setup `warp 2237.1 -2271.5 -32.0 87.7 -27.5 43; nomonsters | 2`. Check: no dotted line across the doorway's ceiling

[report](../../20261024-182730/screenshot.jpg) [NBlood](../../20261024-182730/nblood.jpg) [baseline](../../20261024-182730/baseline.jpg) [now](../../20261024-182730/regress.jpg)

### [20261024-182805](../../20261024-182805/status.md) pickup is seen from shared room

e2m2, same 2026-11-17 12:06 (0.01). `gotobug 20261024-182805`, setup `nomonsters | 2`. Check: the life seed in the room behind the wall does not show at the wall's corner

[report](../../20261024-182805/screenshot.jpg) [NBlood](../../20261024-182805/nblood.jpg) [baseline](../../20261024-182805/baseline.jpg) [now](../../20261024-182805/regress.jpg)

### [20261024-223856](../../20261024-223856/status.md) strange sprite, might be a room in room problem here?

e1m5, same 2026-11-17 12:06 (0.01). `gotobug 20261024-223856`

[report](../../20261024-223856/screenshot.jpg) [NBlood](../../20261024-223856/nblood.jpg) [baseline](../../20261024-223856/baseline.jpg) [now](../../20261024-223856/regress.jpg)

### [20261028-085030](../../20261028-085030/status.md) sprite on bridge seems to be moving when camera is moving creating a jitter effect

e3m3, same 2026-11-17 12:06 (0.02). `gotobug 20261028-085030`

[report](../../20261028-085030/screenshot.jpg) [NBlood](../../20261028-085030/nblood.jpg) [baseline](../../20261028-085030/baseline.jpg) [now](../../20261028-085030/regress.jpg)

### [20261028-174211](../../20261028-174211/status.md) missing water surface?

mrw01, same 2026-11-17 12:06 (0.00). `gotobug 20261028-174211`

[report](../../20261028-174211/screenshot.jpg) [NBlood](../../20261028-174211/nblood.jpg) [baseline](../../20261028-174211/baseline.jpg) [now](../../20261028-174211/regress.jpg)

### [20261029-105717](../../20261029-105717/status.md) incorrect brightness on sprites in the ceiling?

dwe3m1, same 2026-11-17 12:06 (0.01). `gotobug 20261029-105717`

[report](../../20261029-105717/screenshot.jpg) [NBlood](../../20261029-105717/nblood.jpg) [baseline](../../20261029-105717/baseline.jpg) [now](../../20261029-105717/regress.jpg)

### [20261029-152743](../../20261029-152743/status.md) trees are cutoff here

dwe3m1, same 2026-11-17 12:06 (0.07). `gotobug 20261029-152743`

[report](../../20261029-152743/screenshot.jpg) [NBlood](../../20261029-152743/nblood.jpg) [baseline](../../20261029-152743/baseline.jpg) [now](../../20261029-152743/regress.jpg)

### [20261031-083335](../../20261031-083335/status.md) fame is seen here when building is not destroyed

e1m1, same 2026-11-17 12:06 (0.00). `gotobug 20261031-083335`. Check: no fire on the wall left of the gate (fire 711 under 59's floor until the building blows); seen in the recording's replay

[report](../../20261031-083335/screenshot.jpg) [NBlood](../../20261031-083335/nblood.jpg) [baseline](../../20261031-083335/baseline.jpg) [now](../../20261031-083335/regress.jpg)

### [20261031-172211](../../20261031-172211/status.md) napalm sprite get stuck in portal after firing

e2m1, same 2026-11-17 12:06 (0.00). `gotobug 20261031-172211`. Check: fire napalm into the water: no ball left hanging on the surface

[report](../../20261031-172211/screenshot.jpg) [baseline](../../20261031-172211/baseline.jpg) [now](../../20261031-172211/regress.jpg)

### [20261101-133626](../../20261101-133626/status.md) more sprites disappearing at edge of screen

e1m1, same 2026-11-17 12:06 (0.00). `gotobug 20261101-133626`

[report](../../20261101-133626/screenshot.jpg) [NBlood](../../20261101-133626/nblood.jpg) [baseline](../../20261101-133626/baseline.jpg) [now](../../20261101-133626/regress.jpg)

### [20261101-165435](../../20261101-165435/status.md) another portal bug with dynamite getting a copy of the sprite stuck in air

e1m1, same 2026-11-17 12:06 (0.00). `gotobug 20261101-165435`. Check: throw a bundle up through the yard link from the report's spot: one flying bundle, no picture left hanging where it crossed

[report](../../20261101-165435/screenshot.jpg) [baseline](../../20261101-165435/baseline.jpg) [now](../../20261101-165435/regress.jpg)

### [20261105-220910](../../20261105-220910/status.md) shows incorrect sprite that is in a room over room. Zombie should not be visible in the mirror.

test, same 2026-11-17 12:06 (0.03). `gotobug 20261105-220910`

[report](../../20261105-220910/screenshot.jpg) [baseline](../../20261105-220910/baseline.jpg) [now](../../20261105-220910/regress.jpg)

### [20261115-161220](../../20261115-161220/status.md) sprite on vent looks to be offset

sevict, same 2026-11-17 12:06 (0.00). `gotobug 20261115-161220`, setup `cam 2887.82 -2776.23 318.09 4.09 8.25 | 2`. Check: the fan sits in its box on the wall

[report](../../20261115-161220/screenshot.jpg) [NBlood](../../20261115-161220/nblood.jpg) [baseline](../../20261115-161220/baseline.jpg) [now](../../20261115-161220/regress.jpg)

