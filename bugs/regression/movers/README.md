# Regression: movers

Doors, lifts, sliding/rotating/z-motion sectors: trInit and the busy procedures (World.NativeSectors), the moving sectors' meshes and planes (Render.Native).

74 bug(s), 74 in the last run. Rerun: `python pipeline/bugs.py regress movers` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-120044](../../20261015-120044/status.md) texture is missing, I can see through the wall

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-120044`. Check: push the tomb: the pit side under it is textured once it lowers

[report](../../20261015-120044/screenshot.jpg) [NBlood](../../20261015-120044/nblood.jpg) [baseline](../../20261015-120044/baseline.jpg) [now](../../20261015-120044/regress.jpg)

### [20261015-120116](../../20261015-120116/status.md) no collision on the round sliding door, interaction is not working on it as well.

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-120116`. Check: the round door has collision and can be used

[report](../../20261015-120116/screenshot.jpg) [NBlood](../../20261015-120116/nblood.jpg) [baseline](../../20261015-120116/baseline.jpg) [now](../../20261015-120116/regress.jpg)

### [20261015-153003](../../20261015-153003/status.md) Incorrect texture, should be the hedge texture, same as next to it

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-153003`

[report](../../20261015-153003/screenshot.jpg) [NBlood](../../20261015-153003/nblood.jpg) [baseline](../../20261015-153003/baseline.jpg) [now](../../20261015-153003/regress.jpg)

### [20261015-154647](../../20261015-154647/status.md) make the grave open on start like it should

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-154647`

[report](../../20261015-154647/screenshot.jpg) [NBlood](../../20261015-154647/nblood.jpg) [baseline](../../20261015-154647/baseline.jpg) [now](../../20261015-154647/regress.jpg)

### [20261015-161005](../../20261015-161005/status.md) coffing lid is not closed at start

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-161005`

[report](../../20261015-161005/screenshot.jpg) [NBlood](../../20261015-161005/nblood.jpg) [baseline](../../20261015-161005/baseline.jpg) [now](../../20261015-161005/regress.jpg)

### [20261015-212620](../../20261015-212620/status.md) duplicate doors, seems to be overlapping when doors are open.

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-212620`

[report](../../20261015-212620/screenshot.jpg) [NBlood](../../20261015-212620/nblood.jpg) [baseline](../../20261015-212620/baseline.jpg) [now](../../20261015-212620/regress.jpg)

### [20261015-215820](../../20261015-215820/status.md) Duplicate door bug is back

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-215820`

[report](../../20261015-215820/screenshot.jpg) [NBlood](../../20261015-215820/nblood.jpg) [baseline](../../20261015-215820/baseline.jpg) [now](../../20261015-215820/regress.jpg)

### [20261015-230252](../../20261015-230252/status.md) strange doors that should not be here?

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-230252`

[report](../../20261015-230252/screenshot.jpg) [NBlood](../../20261015-230252/nblood.jpg) [baseline](../../20261015-230252/baseline.jpg) [now](../../20261015-230252/regress.jpg)

### [20261015-230345](../../20261015-230345/status.md) blowing a whole in the wall here makes the floor go up?

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-230345`. Check: blow the wall: the floor stays, only the fire sprites rise

[report](../../20261015-230345/screenshot.jpg) [NBlood](../../20261015-230345/nblood.jpg) [baseline](../../20261015-230345/baseline.jpg) [now](../../20261015-230345/regress.jpg)

### [20261015-232356](../../20261015-232356/status.md) Not sure what this gray thing is but it should not be here

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-232356`

[report](../../20261015-232356/screenshot.jpg) [NBlood](../../20261015-232356/nblood.jpg) [baseline](../../20261015-232356/baseline.jpg) [now](../../20261015-232356/regress.jpg)

### [20261015-232421](../../20261015-232421/status.md) curtains should be closed at start and open when interacting

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-232421`. Check: curtains closed at start, open when used

[report](../../20261015-232421/screenshot.jpg) [NBlood](../../20261015-232421/nblood.jpg) [baseline](../../20261015-232421/baseline.jpg) [now](../../20261015-232421/regress.jpg)

### [20261015-232509](../../20261015-232509/status.md) incorrect texture offset, looks like the offset is from when the secret door is open

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-232509`

[report](../../20261015-232509/screenshot.jpg) [NBlood](../../20261015-232509/nblood.jpg) [baseline](../../20261015-232509/baseline.jpg) [now](../../20261015-232509/regress.jpg)

### [20261016-084134](../../20261016-084134/status.md) texture is incorrect scale here, texture should scale when opening the curtains to simulate the folds getting smaller.

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-084134`. Check: open the curtains: the texture squeezes into folds

[report](../../20261016-084134/screenshot.jpg) [NBlood](../../20261016-084134/nblood.jpg) [baseline](../../20261016-084134/baseline.jpg) [now](../../20261016-084134/regress.jpg)

### [20261016-095511](../../20261016-095511/status.md) coffing lid is a bit to high up for some reason, should be flush

e1m1, same 2026-11-15 17:14 (0.05). `gotobug 20261016-095511`, setup `send s30 on | 4`

[report](../../20261016-095511/screenshot.jpg) [NBlood](../../20261016-095511/nblood.jpg) [baseline](../../20261016-095511/baseline.jpg) [now](../../20261016-095511/regress.jpg)

### [20261016-095604](../../20261016-095604/status.md) player pushing against the rotatign door collider prevents it from moving

e1m1, same 2026-11-15 17:14 (0.19). `gotobug 20261016-095604`. Check: stand in the rotating door's way: it still turns

[report](../../20261016-095604/screenshot.jpg) [NBlood](../../20261016-095604/nblood.jpg) [baseline](../../20261016-095604/baseline.jpg) [now](../../20261016-095604/regress.jpg)

### [20261018-152413](../../20261018-152413/status.md) switch pressed but train is not moving, supposed to rotate

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261018-152413`. Check: press the switch: the turntable rotates with its sprites

[report](../../20261018-152413/screenshot.jpg) [NBlood](../../20261018-152413/nblood.jpg) [baseline](../../20261018-152413/baseline.jpg) [now](../../20261018-152413/regress.jpg)

### [20261018-154100](../../20261018-154100/status.md) got stuck in collision from firing and being pushed into the wall

e1m2, same 2026-11-15 17:14 (0.03). `gotobug 20261018-154100`. Check: pushed into the turntable car wall: no getting stuck

[report](../../20261018-154100/screenshot.jpg) [NBlood](../../20261018-154100/nblood.jpg) [baseline](../../20261018-154100/baseline.jpg) [now](../../20261018-154100/regress.jpg)

### [20261018-154403](../../20261018-154403/status.md) I opened the secret but the collision is preventing me from getting the healthpack

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261018-154403`. Check: open the secret: nothing blocks the health pack

[report](../../20261018-154403/screenshot.jpg) [NBlood](../../20261018-154403/nblood.jpg) [baseline](../../20261018-154403/baseline.jpg) [now](../../20261018-154403/regress.jpg)

### [20261018-154455](../../20261018-154455/status.md) part of train sector is not moving

e1m2, same 2026-11-15 17:14 (0.02). `gotobug 20261018-154455`. Check: call the train: the whole middle car arrives

[report](../../20261018-154455/screenshot.jpg) [NBlood](../../20261018-154455/nblood.jpg) [baseline](../../20261018-154455/baseline.jpg) [now](../../20261018-154455/regress.jpg)

### [20261018-154552](../../20261018-154552/status.md) train sector is missing, no collision. same as previous report about not all sectors seems to follow

e1m2, same 2026-11-15 17:14 (0.01). `gotobug 20261018-154552`. Check: call the train: the whole middle car arrives, with collision

[report](../../20261018-154552/screenshot.jpg) [NBlood](../../20261018-154552/nblood.jpg) [baseline](../../20261018-154552/baseline.jpg) [now](../../20261018-154552/regress.jpg)

### [20261018-161128](../../20261018-161128/status.md) can't blow up this wall

e1m2, same 2026-11-15 17:14 (0.01). `gotobug 20261018-161128`. Check: explosives at the wall crack open the wall

[report](../../20261018-161128/screenshot.jpg) [NBlood](../../20261018-161128/nblood.jpg) [baseline](../../20261018-161128/baseline.jpg) [now](../../20261018-161128/regress.jpg)

### [20261018-220309](../../20261018-220309/status.md) door on this side should also follow the pulasating light

e1m2, same 2026-11-15 17:14 (0.08). `gotobug 20261018-220309`

[report](../../20261018-220309/screenshot.jpg) [NBlood](../../20261018-220309/nblood.jpg) [baseline](../../20261018-220309/baseline.jpg) [now](../../20261018-220309/regress.jpg)

### [20261019-104148](../../20261019-104148/status.md) z-fighting doors. looks like the the duplicate open doors remain in the map. Same issue we had in darkplaces a while back.

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261019-104148`

[report](../../20261019-104148/screenshot.jpg) [NBlood](../../20261019-104148/nblood.jpg) [baseline](../../20261019-104148/baseline.jpg) [now](../../20261019-104148/regress.jpg)

### [20261019-113058](../../20261019-113058/status.md) curtains are not rendered correctly

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261019-113058`

[report](../../20261019-113058/screenshot.jpg) [NBlood](../../20261019-113058/nblood.jpg) [baseline](../../20261019-113058/baseline.jpg) [now](../../20261019-113058/regress.jpg)

### [20261019-113857](../../20261019-113857/status.md) looks like there is a missing texture here on the side, we are seeing the wall behind it. Difficult to see this since they are using the same texture.

e1m1, same 2026-11-15 17:14 (0.05). `gotobug 20261019-113857`, setup `send s30 on | 4`

[report](../../20261019-113857/screenshot.jpg) [NBlood](../../20261019-113857/nblood.jpg) [baseline](../../20261019-113857/baseline.jpg) [now](../../20261019-113857/regress.jpg)

### [20261019-130147](../../20261019-130147/status.md) there should be an increase in brightness on the ground when the lid opens

e1m1, same 2026-11-15 17:14 (0.06). `gotobug 20261019-130147`, setup `send s30 on | 4`. Check: the stairwell floor brightens as the coffin lid opens

[report](../../20261019-130147/screenshot.jpg) [NBlood](../../20261019-130147/nblood.jpg) [baseline](../../20261019-130147/baseline.jpg) [now](../../20261019-130147/regress.jpg)

### [20261019-143701](../../20261019-143701/status.md) is this shelf to bright?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261019-143701`

[report](../../20261019-143701/screenshot.jpg) [NBlood](../../20261019-143701/nblood.jpg) [baseline](../../20261019-143701/baseline.jpg) [now](../../20261019-143701/regress.jpg)

### [20261019-144017](../../20261019-144017/status.md) this wall should not be flashing

e1m2, same 2026-11-15 17:14 (0.15). `gotobug 20261019-144017`

[report](../../20261019-144017/screenshot.jpg) [NBlood](../../20261019-144017/nblood.jpg) [baseline](../../20261019-144017/baseline.jpg) [now](../../20261019-144017/regress.jpg)

### [20261019-190046](../../20261019-190046/status.md) duplicate doors and missing?

e1m3, same 2026-11-15 17:14 (0.01). `gotobug 20261019-190046`

[report](../../20261019-190046/screenshot.jpg) [NBlood](../../20261019-190046/nblood.jpg) [baseline](../../20261019-190046/baseline.jpg) [now](../../20261019-190046/regress.jpg)

### [20261019-190113](../../20261019-190113/status.md) opening this vent caude massive stutter

e1m3, same 2026-11-15 17:14 (0.11). `gotobug 20261019-190113`. Check: open the vent: no stutter (perf)

[report](../../20261019-190113/screenshot.jpg) [NBlood](../../20261019-190113/nblood.jpg) [baseline](../../20261019-190113/baseline.jpg) [now](../../20261019-190113/regress.jpg)

### [20261019-210003](../../20261019-210003/status.md) strange geo bug

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-210003`

[report](../../20261019-210003/screenshot.jpg) [NBlood](../../20261019-210003/nblood.jpg) [baseline](../../20261019-210003/baseline.jpg) [now](../../20261019-210003/regress.jpg)

### [20261019-210045](../../20261019-210045/status.md) can see things thru the ceiling

e1m3, same 2026-11-15 17:14 (0.01). `gotobug 20261019-210045`

[report](../../20261019-210045/screenshot.jpg) [NBlood](../../20261019-210045/nblood.jpg) [baseline](../../20261019-210045/baseline.jpg) [now](../../20261019-210045/regress.jpg)

### [20261019-210314](../../20261019-210314/status.md) duplication of the lift exist preventing access to top

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-210314`, setup `send s136 on | 4`. Check: with the lift up, its sides fill the doorway (no invisible face)

[report](../../20261019-210314/screenshot.jpg) [NBlood](../../20261019-210314/nblood.jpg) [baseline](../../20261019-210314/baseline.jpg) [now](../../20261019-210314/regress.jpg)

### [20261020-101910](../../20261020-101910/status.md) strange shading on this

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261020-101910`. Check: play <id> to the end: the turned shelf's side is evenly shaded

[report](../../20261020-101910/screenshot.jpg) [NBlood](../../20261020-101910/nblood.jpg) [baseline](../../20261020-101910/baseline.jpg) [now](../../20261020-101910/regress.jpg)

### [20261021-083036](../../20261021-083036/status.md) this does not look correct? Compare to NBlood

e1m8, same 2026-11-15 17:14 (0.15). `gotobug 20261021-083036`. Check: the corridor strips pinch in with slanted walls for the first seconds, then open; walls pulse with the light like floor and ceiling

[report](../../20261021-083036/screenshot.jpg) [NBlood](../../20261021-083036/nblood.jpg) [baseline](../../20261021-083036/baseline.jpg) [now](../../20261021-083036/regress.jpg)

### [20261023-084101](../../20261023-084101/status.md) the moving sheet does not look correct when it moves to the side. In NBlood it looks like it's moving in sync with the tilting?

e2m6, same 2026-11-15 17:14 (0.11). `gotobug 20261023-084101`, setup `send s0 on | 2`. Check: mid-rise: the floe is one tilted floor with side walls; the water strip opens beside it in step, no flat sheet

[report](../../20261023-084101/screenshot.jpg) [NBlood](../../20261023-084101/nblood.jpg) [baseline](../../20261023-084101/baseline.jpg) [now](../../20261023-084101/regress.jpg)

### [20261023-090620](../../20261023-090620/status.md) whole in geo after iceblock moves

e2m6, same 2026-11-15 17:14 (0.00). `gotobug 20261023-090620`, setup `send s0 on | 12`. Check: after the floe and strips have moved: no hole between the underwater strip's sloped floor, its side wall and the water ceiling

[report](../../20261023-090620/screenshot.jpg) [NBlood](../../20261023-090620/nblood.jpg) [baseline](../../20261023-090620/baseline.jpg) [now](../../20261023-090620/regress.jpg)

### [20261023-123124](../../20261023-123124/status.md) lid looks like just a plane, should look like a box

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261023-123124`. Check: inside the grave looking up while the lid slides: the lid shows its thick side through the portal

[report](../../20261023-123124/screenshot.jpg) [NBlood](../../20261023-123124/nblood.jpg) [baseline](../../20261023-123124/baseline.jpg) [now](../../20261023-123124/regress.jpg)

### [20261023-195622](../../20261023-195622/status.md) here the coffin texture is not following the lid

e1m1, same 2026-11-15 17:14 (0.05). `gotobug 20261023-195622`, setup `send s28 on | 2`. Check: the lid's emblem slides with the lid, whole, not cut by a fixed frame

[report](../../20261023-195622/screenshot.jpg) [NBlood](../../20261023-195622/nblood.jpg) [baseline](../../20261023-195622/baseline.jpg) [now](../../20261023-195622/regress.jpg)

### [20261023-200532](../../20261023-200532/status.md) door hanging down to lower section of train cart

e1m3, same 2026-11-15 17:14 (0.01). `gotobug 20261023-200532`. Check: no wall of the upper corridor's sliding door hangs from the train car's ceiling

[report](../../20261023-200532/screenshot.jpg) [NBlood](../../20261023-200532/nblood.jpg) [baseline](../../20261023-200532/baseline.jpg) [now](../../20261023-200532/regress.jpg)

### [20261023-200544](../../20261023-200544/status.md) whole in geo

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-200544`. Check: the train car's wall tops run straight, no beam or stone poking through

[report](../../20261023-200544/screenshot.jpg) [NBlood](../../20261023-200544/nblood.jpg) [baseline](../../20261023-200544/baseline.jpg) [now](../../20261023-200544/regress.jpg)

### [20261023-200647](../../20261023-200647/status.md) lift platform is not moving just the ceiling?

e1m3, same 2026-11-15 17:14 (0.01). `gotobug 20261023-200647`, setup `use | 4`. Check: press use on the lift: floor and ceiling rise together

[report](../../20261023-200647/screenshot.jpg) [NBlood](../../20261023-200647/nblood.jpg) [baseline](../../20261023-200647/baseline.jpg) [now](../../20261023-200647/regress.jpg)

### [20261023-200801](../../20261023-200801/status.md) some incorrect geo at top of closet door

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-200801`. Check: no beams across the ceiling or wooden wall beside the louvered closet door

[report](../../20261023-200801/screenshot.jpg) [NBlood](../../20261023-200801/nblood.jpg) [baseline](../../20261023-200801/baseline.jpg) [now](../../20261023-200801/regress.jpg)

### [20261023-200849](../../20261023-200849/status.md) missing part when opening door

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-200849`, setup `send 224 off | 8`. Check: with the cars 84/88/92 slid to OFF (over the doorway in Build's impossible space) open the door (send 102): its floor stays whole

[report](../../20261023-200849/screenshot.jpg) [NBlood](../../20261023-200849/nblood.jpg) [baseline](../../20261023-200849/baseline.jpg) [now](../../20261023-200849/regress.jpg)

### [20261023-210146](../../20261023-210146/status.md) duplicate doors here flickering

e1m3, same 2026-11-15 17:14 (0.06). `gotobug 20261023-210146`, setup `send 102 | 2`. Check: both doors open: no second door face flickering in the doorway

[report](../../20261023-210146/screenshot.jpg) [NBlood](../../20261023-210146/nblood.jpg) [baseline](../../20261023-210146/baseline.jpg) [now](../../20261023-210146/regress.jpg)

### [20261023-233102](../../20261023-233102/status.md) small hole/ missing geo when curtains are open

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-233102`

[report](../../20261023-233102/screenshot.jpg) [NBlood](../../20261023-233102/nblood.jpg) [baseline](../../20261023-233102/baseline.jpg) [now](../../20261023-233102/regress.jpg)

### [20261024-095834](../../20261024-095834/status.md) geo sticking up thru the ground, looks like the closet from down below

e1m3, same 2026-11-15 17:14 (0.03). `gotobug 20261024-095834`

[report](../../20261024-095834/screenshot.jpg) [NBlood](../../20261024-095834/nblood.jpg) [baseline](../../20261024-095834/baseline.jpg) [now](../../20261024-095834/regress.jpg)

### [20261024-101111](../../20261024-101111/status.md) this wall looks incorrect

e1m5, same 2026-11-15 17:14 (0.01). `gotobug 20261024-101111`, setup `nomonsters | 2`. Check: the closed sliding panel (ornamented frame) fills the doorway; send s128 on opens it

[report](../../20261024-101111/screenshot.jpg) [NBlood](../../20261024-101111/nblood.jpg) [baseline](../../20261024-101111/baseline.jpg) [now](../../20261024-101111/regress.jpg)

### [20261024-223841](../../20261024-223841/status.md) hole in geo

e1m5, same 2026-11-15 17:14 (0.00). `gotobug 20261024-223841`

[report](../../20261024-223841/screenshot.jpg) [NBlood](../../20261024-223841/nblood.jpg) [baseline](../../20261024-223841/baseline.jpg) [now](../../20261024-223841/regress.jpg)

### [20261025-185658](../../20261025-185658/status.md) wall with no collision, lift? compare to NBlood

e3m5, same 2026-11-15 17:14 (0.09). `gotobug 20261025-185658`, setup `send s8 on | 3`. Check: the open north door shows the lit cabin interior (NBlood with sector 8 on); walking in, the cabin's walls block

[report](../../20261025-185658/screenshot.jpg) [NBlood](../../20261025-185658/nblood.jpg) [baseline](../../20261025-185658/baseline.jpg) [now](../../20261025-185658/regress.jpg)

### [20261025-185719](../../20261025-185719/status.md) very messed up rendering from inside the lift

e3m5, same 2026-11-15 17:14 (0.01). `gotobug 20261025-185719`, setup `send s8 on | 3`. Check: from inside the cabin the corridor north of the open door shows, no lava room

[report](../../20261025-185719/screenshot.jpg) [NBlood](../../20261025-185719/nblood.jpg) [baseline](../../20261025-185719/baseline.jpg) [now](../../20261025-185719/regress.jpg)

### [20261026-093820](../../20261026-093820/status.md) some z-figting on top of the platform, moving the textures don't follow (global mode)

e3m5, same 2026-11-15 17:14 (0.02). `gotobug 20261026-093820`, setup `nomonsters | 0`. Check: pillar tops one texture, no z-fighting; moving, the texture moves with the pillar

[report](../../20261026-093820/screenshot.jpg) [NBlood](../../20261026-093820/nblood.jpg) [baseline](../../20261026-093820/baseline.jpg) [now](../../20261026-093820/regress.jpg)

### [20261026-093911](../../20261026-093911/status.md) missing geo when the turning door is in motion

e3m5, same 2026-11-15 17:14 (0.01). `gotobug 20261026-093911`, setup `send 103 on | 3`. Check: mid-turn: the ring's dark floor before the doorway, no lava

[report](../../20261026-093911/screenshot.jpg) [NBlood](../../20261026-093911/nblood.jpg) [baseline](../../20261026-093911/baseline.jpg) [now](../../20261026-093911/regress.jpg)

### [20261026-120645](../../20261026-120645/status.md) platform should be rotating and moving at the same time

e3m5, same 2026-11-15 17:14 (0.05). `gotobug 20261026-120645`, setup `send 7 on | 1.5`. Check: the pillar is half-turned as it slides (NBlood: it turns 180 degrees across the room)

[report](../../20261026-120645/screenshot.jpg) [NBlood](../../20261026-120645/nblood.jpg) [baseline](../../20261026-120645/baseline.jpg) [now](../../20261026-120645/regress.jpg)

### [20261026-131012](../../20261026-131012/status.md) geo here looks very wrong

e3m1, same 2026-11-15 17:14 (0.01). `gotobug 20261026-131012`, setup `nomonsters | 0`

[report](../../20261026-131012/screenshot.jpg) [NBlood](../../20261026-131012/nblood.jpg) [baseline](../../20261026-131012/baseline.jpg) [now](../../20261026-131012/regress.jpg)

### [20261026-140737](../../20261026-140737/status.md) texture is upside down

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-140737`, setup `nomonsters | 0`

[report](../../20261026-140737/screenshot.jpg) [NBlood](../../20261026-140737/nblood.jpg) [baseline](../../20261026-140737/baseline.jpg) [now](../../20261026-140737/regress.jpg)

### [20261026-141026](../../20261026-141026/status.md) invisible collision after raising floor, can't progress

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-141026`, setup `nomonsters; send 117 on | 5`. Check: walk up the raised crates from the report spot (south)

[report](../../20261026-141026/screenshot.jpg) [NBlood](../../20261026-141026/nblood.jpg) [baseline](../../20261026-141026/baseline.jpg) [now](../../20261026-141026/regress.jpg)

### [20261026-143245](../../20261026-143245/status.md) fell of of world

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-143245`, setup `nomonsters; send 117 on | 6`. Check: from room 141 (2134 -1137, floor -56) walk east under the raised crates: blocked, no fall

[report](../../20261026-143245/screenshot.jpg) [NBlood](../../20261026-143245/nblood.jpg) [baseline](../../20261026-143245/baseline.jpg) [now](../../20261026-143245/regress.jpg)

### [20261026-143500](../../20261026-143500/status.md) incorrect geo

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-143500`, setup `nomonsters | 0`

[report](../../20261026-143500/screenshot.jpg) [NBlood](../../20261026-143500/nblood.jpg) [baseline](../../20261026-143500/baseline.jpg) [now](../../20261026-143500/regress.jpg)

### [20261026-181208](../../20261026-181208/status.md) floating geo

e3m3, same 2026-11-15 17:14 (0.00). `gotobug 20261026-181208`

[report](../../20261026-181208/screenshot.jpg) [NBlood](../../20261026-181208/nblood.jpg) [baseline](../../20261026-181208/baseline.jpg) [now](../../20261026-181208/regress.jpg)

### [20261027-092415](../../20261027-092415/status.md) collision on moving platform here stopped working

e3m5, same 2026-11-15 17:14 (0.00). `gotobug 20261027-092415`. Check: jump east from the ledge onto the sliding platform (e3m5-platform): you land on it, not through the shaft's link

[report](../../20261027-092415/screenshot.jpg) [baseline](../../20261027-092415/baseline.jpg) [now](../../20261027-092415/regress.jpg)

### [20261027-092938](../../20261027-092938/status.md) hole in geo

e1m4, same 2026-11-15 17:14 (0.00). `gotobug 20261027-092938`

[report](../../20261027-092938/screenshot.jpg) [NBlood](../../20261027-092938/nblood.jpg) [baseline](../../20261027-092938/baseline.jpg) [now](../../20261027-092938/regress.jpg)

### [20261027-093224](../../20261027-093224/status.md) dont rotate with carousle

e1m4, same 2026-11-15 17:14 (0.02). `gotobug 20261027-093224`. Check: walk from room 352 onto the carousel (e1m4-carousel): standing still, it carries you round, feet at -128

[report](../../20261027-093224/screenshot.jpg) [baseline](../../20261027-093224/baseline.jpg) [now](../../20261027-093224/regress.jpg)

### [20261027-202414](../../20261027-202414/status.md) hole in geo

e4m2, same 2026-11-15 17:14 (0.02). `gotobug 20261027-202414`. Check: the gate leaves start closed across the corridor (OFF), floor whole; send 117 on swings them open against the side walls

[report](../../20261027-202414/screenshot.jpg) [NBlood](../../20261027-202414/nblood.jpg) [baseline](../../20261027-202414/baseline.jpg) [now](../../20261027-202414/regress.jpg)

### [20261027-202540](../../20261027-202540/status.md) hole in geo most likley due to the previous report

e4m2, same 2026-11-15 17:14 (0.00). `gotobug 20261027-202540`, setup `send 116 on | 12`. Check: after the round room 33 and room 201 turn: the stairs whole, no holes

[report](../../20261027-202540/screenshot.jpg) [NBlood](../../20261027-202540/nblood.jpg) [baseline](../../20261027-202540/baseline.jpg) [now](../../20261027-202540/regress.jpg)

### [20261029-125840](../../20261029-125840/status.md) shore is white when riding boat

dwe3m1, same 2026-11-15 17:14 (0.15). `gotobug 20261029-125840`

[report](../../20261029-125840/screenshot.jpg) [NBlood](../../20261029-125840/nblood.jpg) [baseline](../../20261029-125840/baseline.jpg) [now](../../20261029-125840/regress.jpg)

### [20261029-153012](../../20261029-153012/status.md) missing ground when opening door

dwe3m1, same 2026-11-15 17:14 (0.02). `gotobug 20261029-153012`

[report](../../20261029-153012/screenshot.jpg) [baseline](../../20261029-153012/baseline.jpg) [now](../../20261029-153012/regress.jpg)

### [20261029-202850](../../20261029-202850/status.md) missing geo when train comes in, only when moving in some areas

e1m2, same 2026-11-15 17:14 (0.01). `gotobug 20261029-202850`, setup `send 111 | 8.2`. Check: train mid-arrival: the car's side whole between the pillars, no stone strip beside the cart

[report](../../20261029-202850/screenshot.jpg) [NBlood](../../20261029-202850/nblood.jpg) [baseline](../../20261029-202850/baseline.jpg) [now](../../20261029-202850/regress.jpg)

### [20261030-113748](../../20261030-113748/status.md) have to spam click to get boat going?

dwe3m1, same 2026-11-15 17:14 (0.02). `gotobug 20261030-113748`, setup `nomonsters | 0`. Check: crouch in the boat facing the lever panel and press use once: the boat sets off

[report](../../20261030-113748/screenshot.jpg) [baseline](../../20261030-113748/baseline.jpg) [now](../../20261030-113748/regress.jpg)

### [20261030-171348](../../20261030-171348/status.md) hole in geo herew

e2m1, same 2026-11-15 17:14 (0.05). `gotobug 20261030-171348`, setup `b3d_fixblood 0; send 115 off; send 100 off | 5`. Check: after the ice block slides off: water where it stood, no hole (also with transparent water)

[report](../../20261030-171348/screenshot.jpg) [NBlood](../../20261030-171348/nblood.jpg) [baseline](../../20261030-171348/baseline.jpg) [now](../../20261030-171348/regress.jpg)

### [20261031-092746](../../20261031-092746/status.md) human at start got stuck, it should collide with the train

e1m3, same 2026-11-15 17:14 (0.04). `gotobug 20261031-092746`. Check: the human on the tracks comes down to the train and bursts there about 5 s in, then the next two props in turn

[report](../../20261031-092746/screenshot.jpg) [NBlood](../../20261031-092746/nblood.jpg) [baseline](../../20261031-092746/baseline.jpg) [now](../../20261031-092746/regress.jpg)

### [20261031-113433](../../20261031-113433/status.md) gaps in water geo when boat is moving

dwe3m1, same 2026-11-15 17:14 (0.00). `gotobug 20261031-113433`. Check: ride the boat and open doors: no long thin lines across the water or the floor beside them while they move

[report](../../20261031-113433/screenshot.jpg) [baseline](../../20261031-113433/baseline.jpg) [now](../../20261031-113433/regress.jpg)

### [20261031-113518](../../20261031-113518/status.md) same bug when dooor is moving

dwe3m1, same 2026-11-15 17:14 (0.00). `gotobug 20261031-113518`. Check: open the door: no long thin line across the yard's floor while it moves

[report](../../20261031-113518/screenshot.jpg) [baseline](../../20261031-113518/baseline.jpg) [now](../../20261031-113518/regress.jpg)

### [20261101-191449](../../20261101-191449/status.md) small strip of missing geo here when door is rotating

e3m5, same 2026-11-15 17:14 (0.00). `gotobug 20261101-191449`. Check: bugs.py motion holds this door against the unculled exact picture: no strip of the room behind cut away

[report](../../20261101-191449/screenshot.jpg) [baseline](../../20261101-191449/baseline.jpg) [now](../../20261101-191449/regress.jpg)

