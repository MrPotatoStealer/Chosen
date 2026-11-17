# Regression: room-over-room

Room-over-room links (Blood stack links): portal rendering and alignment (PortalRenderer, World.NativeLinks, the eye crossing links in Game.cs), sprites and lights seen through portals.

40 bug(s), 40 in the last run. Rerun: `python pipeline/bugs.py regress room-over-room` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-153034](../../20261015-153034/status.md) Incorrect texture, should be sky, instead its some sort of wood

e1m1, same 2026-11-17 11:24 (0.03). `gotobug 20261015-153034`

[report](../../20261015-153034/screenshot.jpg) [NBlood](../../20261015-153034/nblood.jpg) [baseline](../../20261015-153034/baseline.jpg) [now](../../20261015-153034/regress.jpg)

### [20261015-153113](../../20261015-153113/status.md) whole in map? misaligned vertices?

e1m1, same 2026-11-17 11:24 (0.03). `gotobug 20261015-153113`

[report](../../20261015-153113/screenshot.jpg) [NBlood](../../20261015-153113/nblood.jpg) [baseline](../../20261015-153113/baseline.jpg) [now](../../20261015-153113/regress.jpg)

### [20261015-153138](../../20261015-153138/status.md) whole in map, missing texture

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261015-153138`

[report](../../20261015-153138/screenshot.jpg) [NBlood](../../20261015-153138/nblood.jpg) [baseline](../../20261015-153138/baseline.jpg) [now](../../20261015-153138/regress.jpg)

### [20261015-155021](../../20261015-155021/status.md) still a small hole here in the geometry, it should be filled

e1m1, same 2026-11-17 11:24 (0.02). `gotobug 20261015-155021`

[report](../../20261015-155021/screenshot.jpg) [NBlood](../../20261015-155021/nblood.jpg) [baseline](../../20261015-155021/baseline.jpg) [now](../../20261015-155021/regress.jpg)

### [20261015-155049](../../20261015-155049/status.md) incorrect texture, should be same as the walls next to it

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261015-155049`

[report](../../20261015-155049/screenshot.jpg) [NBlood](../../20261015-155049/nblood.jpg) [baseline](../../20261015-155049/baseline.jpg) [now](../../20261015-155049/regress.jpg)

### [20261015-161128](../../20261015-161128/status.md) strange geometry sticking out from wall, it should not bo visible

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261015-161128`

[report](../../20261015-161128/screenshot.jpg) [NBlood](../../20261015-161128/nblood.jpg) [baseline](../../20261015-161128/baseline.jpg) [now](../../20261015-161128/regress.jpg)

### [20261015-161312](../../20261015-161312/status.md) skewed gateway for some reason, I guess misaligment between two room over room that you are trying to match up but incorrectly

e1m1, same 2026-11-17 11:24 (0.02). `gotobug 20261015-161312`

[report](../../20261015-161312/screenshot.jpg) [NBlood](../../20261015-161312/nblood.jpg) [baseline](../../20261015-161312/baseline.jpg) [now](../../20261015-161312/regress.jpg)

### [20261015-163721](../../20261015-163721/status.md) skewed geometry, it should be straight

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261015-163721`

[report](../../20261015-163721/screenshot.jpg) [NBlood](../../20261015-163721/nblood.jpg) [baseline](../../20261015-163721/baseline.jpg) [now](../../20261015-163721/regress.jpg)

### [20261015-163742](../../20261015-163742/status.md) geomoetry is poking out from the wall

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261015-163742`

[report](../../20261015-163742/screenshot.jpg) [NBlood](../../20261015-163742/nblood.jpg) [baseline](../../20261015-163742/baseline.jpg) [now](../../20261015-163742/regress.jpg)

### [20261015-212742](../../20261015-212742/status.md) portals have small gap on don't align perfectly

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261015-212742`

[report](../../20261015-212742/screenshot.jpg) [NBlood](../../20261015-212742/nblood.jpg) [baseline](../../20261015-212742/baseline.jpg) [now](../../20261015-212742/regress.jpg)

### [20261016-084017](../../20261016-084017/status.md) jumping next to the gate (sprite) here and you are teleported to other side of the sprite in the air for a few frames

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261016-084017`. Check: jump beside the gate: the view stays on this side

[report](../../20261016-084017/screenshot.jpg) [NBlood](../../20261016-084017/nblood.jpg) [baseline](../../20261016-084017/baseline.jpg) [now](../../20261016-084017/regress.jpg)

### [20261016-095511](../../20261016-095511/status.md) coffing lid is a bit to high up for some reason, should be flush

e1m1, same 2026-11-17 11:24 (0.05). `gotobug 20261016-095511`, setup `send s30 on | 4`

[report](../../20261016-095511/screenshot.jpg) [NBlood](../../20261016-095511/nblood.jpg) [baseline](../../20261016-095511/baseline.jpg) [now](../../20261016-095511/regress.jpg)

### [20261016-111009](../../20261016-111009/status.md) sprite is in wrong location when seen thru a portal, you can see other sprites liek spiderwebs also shifting location

e1m1, same 2026-11-17 11:24 (0.06). `gotobug 20261016-111009`, setup `send s30 on | 4`

[report](../../20261016-111009/screenshot.jpg) [NBlood](../../20261016-111009/nblood.jpg) [baseline](../../20261016-111009/baseline.jpg) [now](../../20261016-111009/regress.jpg)

### [20261016-184910](../../20261016-184910/status.md) OutOf Bounds collision jump

e1m1, same 2026-11-17 11:24 (0.08). `gotobug 20261016-184910`, setup `send s30 on | 4`. Check: fall back through the coffin portal near its edge: no out of bounds

[report](../../20261016-184910/screenshot.jpg) [NBlood](../../20261016-184910/nblood.jpg) [baseline](../../20261016-184910/baseline.jpg) [now](../../20261016-184910/regress.jpg)

### [20261018-154642](../../20261018-154642/status.md) collision still exist after blowing up the ground here

e1m2, same 2026-11-17 11:24 (0.00). `gotobug 20261018-154642`. Check: blow up the ground: no invisible floor left

[report](../../20261018-154642/screenshot.jpg) [NBlood](../../20261018-154642/nblood.jpg) [baseline](../../20261018-154642/baseline.jpg) [now](../../20261018-154642/regress.jpg)

### [20261019-091142](../../20261019-091142/status.md) portal is not rendering correctly

e1m1, same 2026-11-17 11:24 (0.05). `gotobug 20261019-091142`, setup `send s30 on | 4`

[report](../../20261019-091142/screenshot.jpg) [NBlood](../../20261019-091142/nblood.jpg) [baseline](../../20261019-091142/baseline.jpg) [now](../../20261019-091142/regress.jpg)

### [20261019-102445](../../20261019-102445/status.md) portal rendering is strange when getting close to the portal when jumping for a frame or so, just as camera is between I guess?

e1m1, same 2026-11-17 11:24 (0.05). `gotobug 20261019-102445`, setup `send s30 on | 4`. Check: jump under the stairwell portal: no frame drawn from inside the ceiling

[report](../../20261019-102445/screenshot.jpg) [NBlood](../../20261019-102445/nblood.jpg) [baseline](../../20261019-102445/baseline.jpg) [now](../../20261019-102445/regress.jpg)

### [20261019-104046](../../20261019-104046/status.md) missing sprite when viewed thru portal

e1m1, same 2026-11-17 11:24 (0.06). `gotobug 20261019-104046`, setup `send s30 on | 4`

[report](../../20261019-104046/screenshot.jpg) [NBlood](../../20261019-104046/nblood.jpg) [baseline](../../20261019-104046/baseline.jpg) [now](../../20261019-104046/regress.jpg)

### [20261019-113857](../../20261019-113857/status.md) looks like there is a missing texture here on the side, we are seeing the wall behind it. Difficult to see this since they are using the same texture.

e1m1, same 2026-11-17 11:24 (0.05). `gotobug 20261019-113857`, setup `send s30 on | 4`

[report](../../20261019-113857/screenshot.jpg) [NBlood](../../20261019-113857/nblood.jpg) [baseline](../../20261019-113857/baseline.jpg) [now](../../20261019-113857/regress.jpg)

### [20261019-122928](../../20261019-122928/status.md) sprites appearing and disappearing based on distance

e1m1, same 2026-11-17 11:24 (0.05). `gotobug 20261019-122928`

[report](../../20261019-122928/screenshot.jpg) [NBlood](../../20261019-122928/nblood.jpg) [baseline](../../20261019-122928/baseline.jpg) [now](../../20261019-122928/regress.jpg)

### [20261019-130147](../../20261019-130147/status.md) there should be an increase in brightness on the ground when the lid opens

e1m1, same 2026-11-17 11:24 (0.06). `gotobug 20261019-130147`, setup `send s30 on | 4`. Check: the stairwell floor brightens as the coffin lid opens

[report](../../20261019-130147/screenshot.jpg) [NBlood](../../20261019-130147/nblood.jpg) [baseline](../../20261019-130147/baseline.jpg) [now](../../20261019-130147/regress.jpg)

### [20261019-162448](../../20261019-162448/status.md) light is not replicated through portal

e1m1, same 2026-11-17 11:24 (0.03). `gotobug 20261019-162448`

[report](../../20261019-162448/screenshot.jpg) [NBlood](../../20261019-162448/nblood.jpg) [baseline](../../20261019-162448/baseline.jpg) [now](../../20261019-162448/regress.jpg)

### [20261019-162631](../../20261019-162631/status.md) sprite shift offset when rendering through portal

e1m1, same 2026-11-17 11:24 (0.03). `gotobug 20261019-162631`

[report](../../20261019-162631/screenshot.jpg) [NBlood](../../20261019-162631/nblood.jpg) [baseline](../../20261019-162631/baseline.jpg) [now](../../20261019-162631/regress.jpg)

### [20261019-181609](../../20261019-181609/status.md) lights should not be active until the sprite itself is active, and destroyed if the sprite is destroyed

e1m1, same 2026-11-17 11:24 (0.06). `gotobug 20261019-181609`

[report](../../20261019-181609/screenshot.jpg) [NBlood](../../20261019-181609/nblood.jpg) [baseline](../../20261019-181609/baseline.jpg) [now](../../20261019-181609/regress.jpg)

### [20261019-181718](../../20261019-181718/status.md) no shadows even when shadow is turned on. I don't expect to see the light go thru the wall like this

e1m1, same 2026-11-17 11:24 (0.01). `gotobug 20261019-181718`

[report](../../20261019-181718/screenshot.jpg) [NBlood](../../20261019-181718/nblood.jpg) [baseline](../../20261019-181718/baseline.jpg) [now](../../20261019-181718/regress.jpg)

### [20261019-205912](../../20261019-205912/status.md) performance issue looking this way

e1m3, same 2026-11-17 11:24 (0.00). `gotobug 20261019-205912`. Check: perf looking this way (portal behind walls not drawn)

[report](../../20261019-205912/screenshot.jpg) [NBlood](../../20261019-205912/nblood.jpg) [baseline](../../20261019-205912/baseline.jpg) [now](../../20261019-205912/regress.jpg)

### [20261020-231602](../../20261020-231602/status.md) something is not correct with this geo, room in room issue?

e1m6, same 2026-11-17 11:24 (0.08). `gotobug 20261020-231602`

[report](../../20261020-231602/screenshot.jpg) [NBlood](../../20261020-231602/nblood.jpg) [baseline](../../20261020-231602/baseline.jpg) [now](../../20261020-231602/regress.jpg)

### [20261021-084032](../../20261021-084032/status.md) some more geo issues here, room in room?

e1m6, same 2026-11-17 11:24 (0.08). `gotobug 20261021-084032`

[report](../../20261021-084032/screenshot.jpg) [NBlood](../../20261021-084032/nblood.jpg) [baseline](../../20261021-084032/baseline.jpg) [now](../../20261021-084032/regress.jpg)

### [20261023-123124](../../20261023-123124/status.md) lid looks like just a plane, should look like a box

e1m1, same 2026-11-17 11:24 (0.00). `gotobug 20261023-123124`. Check: inside the grave looking up while the lid slides: the lid shows its thick side through the portal

[report](../../20261023-123124/screenshot.jpg) [NBlood](../../20261023-123124/nblood.jpg) [baseline](../../20261023-123124/baseline.jpg) [now](../../20261023-123124/regress.jpg)

### [20261023-181659](../../20261023-181659/status.md) z-fighting texture

e1m1, same 2026-11-17 11:24 (0.00). `gotobug 20261023-181659`, setup `send s30 on | 5`. Check: look into the opened coffin from above: its rim edges must not flicker

[report](../../20261023-181659/screenshot.jpg) [NBlood](../../20261023-181659/nblood.jpg) [baseline](../../20261023-181659/baseline.jpg) [now](../../20261023-181659/regress.jpg)

### [20261023-224613](../../20261023-224613/status.md) why is there two keys here?

e1m8, same 2026-11-17 11:24 (0.09). `gotobug 20261023-224613`, setup `nomonsters | 2`. Check: one skull key and one chest on the walkway

[report](../../20261023-224613/screenshot.jpg) [NBlood](../../20261023-224613/nblood.jpg) [baseline](../../20261023-224613/baseline.jpg) [now](../../20261023-224613/regress.jpg)

### [20261026-130935](../../20261026-130935/status.md) I can see the wrong room here, not sure if the collision is correct

e3m1, same 2026-11-17 11:24 (0.01). `gotobug 20261026-130935`, setup `nomonsters | 0`

[report](../../20261026-130935/screenshot.jpg) [NBlood](../../20261026-130935/nblood.jpg) [baseline](../../20261026-130935/baseline.jpg) [now](../../20261026-130935/regress.jpg)

### [20261026-180722](../../20261026-180722/status.md) some glitches here going down into the water sometimes, like 1 frame the wrong stuff is rendered

e3m3, same 2026-11-17 11:24 (0.00). `gotobug 20261026-180722`. Check: walk along the flooded stairs with the eye just over the surface: the room above never drops out for a frame

[report](../../20261026-180722/screenshot.jpg) [NBlood](../../20261026-180722/nblood.jpg) [baseline](../../20261026-180722/baseline.jpg) [now](../../20261026-180722/regress.jpg)

### [20261028-085030](../../20261028-085030/status.md) sprite on bridge seems to be moving when camera is moving creating a jitter effect

e3m3, same 2026-11-17 11:24 (0.02). `gotobug 20261028-085030`

[report](../../20261028-085030/screenshot.jpg) [NBlood](../../20261028-085030/nblood.jpg) [baseline](../../20261028-085030/baseline.jpg) [now](../../20261028-085030/regress.jpg)

### [20261028-103255](../../20261028-103255/status.md) here is the part where flickering happening

e3m3, same 2026-11-17 11:24 (0.01). `gotobug 20261028-103255`. Check: crouch-swim on the top flooded step (e3m3-stairs, hold crouch): the room above never drops out

[report](../../20261028-103255/screenshot.jpg) [baseline](../../20261028-103255/baseline.jpg) [now](../../20261028-103255/regress.jpg)

### [20261028-174211](../../20261028-174211/status.md) missing water surface?

mrw01, same 2026-11-17 11:24 (0.00). `gotobug 20261028-174211`

[report](../../20261028-174211/screenshot.jpg) [NBlood](../../20261028-174211/nblood.jpg) [baseline](../../20261028-174211/baseline.jpg) [now](../../20261028-174211/regress.jpg)

### [20261031-083335](../../20261031-083335/status.md) fame is seen here when building is not destroyed

e1m1, same 2026-11-17 11:24 (0.00). `gotobug 20261031-083335`. Check: no fire on the wall left of the gate (fire 711 under 59's floor until the building blows); seen in the recording's replay

[report](../../20261031-083335/screenshot.jpg) [NBlood](../../20261031-083335/nblood.jpg) [baseline](../../20261031-083335/baseline.jpg) [now](../../20261031-083335/regress.jpg)

### [20261031-093142](../../20261031-093142/status.md) incorrect geo a piece is seen that should not be visible

e3m1, same 2026-11-17 11:24 (0.00). `gotobug 20261031-093142`

[report](../../20261031-093142/screenshot.jpg) [NBlood](../../20261031-093142/nblood.jpg) [baseline](../../20261031-093142/baseline.jpg) [now](../../20261031-093142/regress.jpg)

### [20261101-151653](../../20261101-151653/status.md) hole in geo here now

e1m1, same 2026-11-17 11:24 (0.00). `gotobug 20261101-151653`, setup `send s30 on | 4`. Check: the chapel's ceiling whole from under the coffin's link, no triangle of sky at the crosshair

[report](../../20261101-151653/screenshot.jpg) [baseline](../../20261101-151653/baseline.jpg) [now](../../20261101-151653/regress.jpg)

### [20261115-161414](../../20261115-161414/status.md) black room over room. Rendering not working correctly here

sevict, same 2026-11-17 11:24 (0.00). `gotobug 20261115-161414`, setup `cam 1569.45 -1943.50 261.94 328.89 17.79 | 2`. Check: the street below shows through the balcony's floor

[report](../../20261115-161414/screenshot.jpg) [NBlood](../../20261115-161414/nblood.jpg) [baseline](../../20261115-161414/baseline.jpg) [now](../../20261115-161414/regress.jpg)

