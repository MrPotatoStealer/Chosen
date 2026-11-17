# Bug reports

Made in game with `reportbug <what is wrong>`; `gotobug <id>` returns to the spot. Managed with `pipeline/bugs.py` (import, list, repro, fix). Maps ending in .map were reported in the Godot game. Tricky bugs are also grouped by the code they exercise in [regression](regression/README.md).

| Id | Status | Map | Description |
|---|---|---|---|
| [20261015-120018](20261015-120018/status.md) | fixed | maps/e1m1.bsp | walltexture is darker then the nearby walls. |
| [20261015-120044](20261015-120044/status.md) | fixed | maps/e1m1.bsp | texture is missing, I can see through the wall |
| [20261015-120116](20261015-120116/status.md) | fixed | maps/e1m1.bsp | no collision on the round sliding door, interaction is not working on it as well. |
| [20261015-120214](20261015-120214/status.md) | fixed | maps/e1m1.bsp | wall have incorrect texture or missing, Its solid but should the the same as the walls next to it. |
| [20261015-120249](20261015-120249/status.md) | fixed | maps/e1m1.bsp | wall is missing texture, I can see through the wall. Collision seems to be correct |
| [20261015-152927](20261015-152927/status.md) | fixed | maps/e1m1.bsp | Missing texture, can see to the other side, should be the same as the walls next to it. |
| [20261015-153003](20261015-153003/status.md) | fixed | maps/e1m1.bsp | Incorrect texture, should be the hedge texture, same as next to it |
| [20261015-153034](20261015-153034/status.md) | fixed | maps/e1m1.bsp | Incorrect texture, should be sky, instead its some sort of wood |
| [20261015-153113](20261015-153113/status.md) | fixed | maps/e1m1.bsp | whole in map? misaligned vertices? |
| [20261015-153138](20261015-153138/status.md) | fixed | maps/e1m1.bsp | whole in map, missing texture |
| [20261015-154615](20261015-154615/status.md) | fixed | maps/e1m1.bsp | should be darker like the surrounding area in the grave |
| [20261015-154647](20261015-154647/status.md) | fixed | maps/e1m1.bsp | make the grave open on start like it should |
| [20261015-154734](20261015-154734/status.md) | fixed | maps/e1m1.bsp | you can see distance objects in the sky, there needs to be some occlusion of the sky boxes as well to prevent this |
| [20261015-154822](20261015-154822/status.md) | fixed | maps/e1m1.bsp | zombies should wake up from the graves when walking to this area |
| [20261015-154927](20261015-154927/status.md) | fixed | maps/e1m1.bsp | this part of the hedge should look a bit different from the rest to hint that its a secret behind |
| [20261015-154952](20261015-154952/status.md) | fixed | maps/e1m1.bsp | gates are open from the start |
| [20261015-155021](20261015-155021/status.md) | fixed | maps/e1m1.bsp | still a small hole here in the geometry, it should be filled |
| [20261015-155049](20261015-155049/status.md) | fixed | maps/e1m1.bsp | incorrect texture, should be same as the walls next to it |
| [20261015-155131](20261015-155131/status.md) | fixed | maps/e1m1.bsp | this area is all skewed for some reason, it should be straight. |
| [20261015-161005](20261015-161005/status.md) | fixed | maps/e1m1.bsp | coffing lid is not closed at start |
| [20261015-161045](20261015-161045/status.md) | fixed | maps/e1m1.bsp | gates are closed but has no collision |
| [20261015-161102](20261015-161102/status.md) | fixed | maps/e1m1.bsp | window should be a bit transparent |
| [20261015-161128](20261015-161128/status.md) | fixed | maps/e1m1.bsp | strange geometry sticking out from wall, it should not bo visible |
| [20261015-161312](20261015-161312/status.md) | fixed | maps/e1m1.bsp | skewed gateway for some reason, I guess misaligment between two room over room that you are trying to match up but incorrectly |
| [20261015-163721](20261015-163721/status.md) | fixed | maps/e1m1.bsp | skewed geometry, it should be straight |
| [20261015-163742](20261015-163742/status.md) | fixed | maps/e1m1.bsp | geomoetry is poking out from the wall |
| [20261015-163841](20261015-163841/status.md) | fixed | maps/e1m1.bsp | this whole in the map is still not fixed, I can see the sky between the ground, for easier replication make sky purple or something while testing. |
| [20261015-212620](20261015-212620/status.md) | fixed | maps/e1m1.bsp | duplicate doors, seems to be overlapping when doors are open. |
| [20261015-212742](20261015-212742/status.md) | fixed | maps/e1m1.bsp | portals have small gap on don't align perfectly |
| [20261015-215801](20261015-215801/status.md) | fixed | maps/e1m1.bsp | Another grap in geo |
| [20261015-215820](20261015-215820/status.md) | fixed | maps/e1m1.bsp | Duplicate door bug is back |
| [20261015-230156](20261015-230156/status.md) | fixed | maps/e1m1.bsp | piano secret not working, |
| [20261015-230252](20261015-230252/status.md) | fixed | maps/e1m1.bsp | strange doors that should not be here? |
| [20261015-230345](20261015-230345/status.md) | fixed | maps/e1m1.bsp | blowing a whole in the wall here makes the floor go up? |
| [20261015-230458](20261015-230458/status.md) | fixed | maps/e1m1.bsp | strange collision here, I can crouch and walk thru the gap |
| [20261015-232326](20261015-232326/status.md) | fixed | maps/e1m1.bsp | fire should not be visible before blowing up the funeral home |
| [20261015-232356](20261015-232356/status.md) | fixed | maps/e1m1.bsp | Not sure what this gray thing is but it should not be here |
| [20261015-232421](20261015-232421/status.md) | fixed | maps/e1m1.bsp | curtains should be closed at start and open when interacting |
| [20261015-232509](20261015-232509/status.md) | fixed | maps/e1m1.bsp | incorrect texture offset, looks like the offset is from when the secret door is open |
| [20261015-232559](20261015-232559/status.md) | fixed | maps/e1m1.bsp | lightswitctes are not working, should turn on lights on interaction |
| [20261015-232646](20261015-232646/status.md) | fixed | maps/e1m1.bsp | missing firsre texture hiding the inner part (secret) of the oven |
| [20261015-232759](20261015-232759/status.md) | fixed | maps/e1m1.bsp | secret is revelead message is triggered before finding the secret (behind this object) |
| [20261015-232829](20261015-232829/status.md) | fixed | maps/e1m1.bsp | missing texture hiding the secret (cove) in the wall |
| [20261016-084017](20261016-084017/status.md) | fixed | maps/e1m1.bsp | jumping next to the gate (sprite) here and you are teleported to other side of the sprite in the air for a few frames |
| [20261016-084134](20261016-084134/status.md) | fixed | maps/e1m1.bsp | texture is incorrect scale here, texture should scale when opening the curtains to simulate the folds getting smaller. |
| [20261016-084220](20261016-084220/status.md) | fixed | maps/e1m1.bsp | small gap in geo |
| [20261016-084242](20261016-084242/status.md) | fixed | maps/e1m1.bsp | small gap in geo, vertices needs to be merged? |
| [20261016-084332](20261016-084332/status.md) | fixed | maps/e1m1.bsp | incorrect light on secret that can be blasted open, I guess because of light changes with lightswitch |
| [20261016-084440](20261016-084440/status.md) | fixed | maps/e1m1.bsp | standing in this area should not be possible, should remain crouched? |
| [20261016-084631](20261016-084631/status.md) | fixed | maps/e1m1.bsp | can't shoot thru the window? |
| [20261016-084649](20261016-084649/status.md) | fixed | maps/e1m1.bsp | also can't jump thru the window |
| [20261016-092337](20261016-092337/status.md) | fixed | maps/e1m1.bsp | whole in geo, missing texture? |
| [20261016-095511](20261016-095511/status.md) | fixed | maps/E1M1.bsp | coffing lid is a bit to high up for some reason, should be flush |
| [20261016-095536](20261016-095536/status.md) | fixed | maps/E1M1.bsp | sprites missing a bit of transparency |
| [20261016-095604](20261016-095604/status.md) | fixed | maps/E1M1.bsp | player pushing against the rotatign door collider prevents it from moving |
| [20261016-095646](20261016-095646/status.md) | fixed | maps/E1M1.bsp | zombie spawns on top of grave, should be on the ground |
| [20261016-095818](20261016-095818/status.md) | fixed | maps/E1M1.bsp | small tiney gaps all over the level, build might have truncated the vertices to make them align perfectly? |
| [20261016-095924](20261016-095924/status.md) | fixed | maps/E1M1.bsp | more ugly tiney gaps |
| [20261016-100021](20261016-100021/status.md) | fixed | maps/E1M1.bsp | sprites are not animated, also this fire sprite should be brighter |
| [20261016-100058](20261016-100058/status.md) | fixed | maps/E1M1.bsp | vase requires multiple hits to destroy with pitchfork, incorrect damage or hp? |
| [20261016-100306](20261016-100306/status.md) | fixed | maps/E1M1.bsp | can jump up this ledge, there is almost like a ledge grab happening pushing the player up. I guess this is because player is using a capsule collider where original is just a position? |
| [20261016-110712](20261016-110712/status.md) | fixed | maps/E1M1.bsp | burning tree animations make it move sideways during some of it frames. Like center is shifted |
| [20261016-110833](20261016-110833/status.md) | fixed | maps/E1M1.bsp | fullbright of the fire is gone again |
| [20261016-111009](20261016-111009/status.md) | fixed | maps/E1M1.bsp | sprite is in wrong location when seen thru a portal, you can see other sprites liek spiderwebs also shifting location |
| [20261016-153928](20261016-153928/status.md) | fixed | maps/E1M1.bsp | strange shading on the curtains |
| [20261016-154112](20261016-154112/status.md) | fixed | maps/E1M1.bsp | you can stand on the small collision here, in NBlood it should not be possible and you fall to ground |
| [20261016-184910](20261016-184910/status.md) | fixed | maps/E1M1.bsp | OutOf Bounds collision jump |
| [20261018-152413](20261018-152413/status.md) | fixed | maps/E1M2.bsp | switch pressed but train is not moving, supposed to rotate |
| [20261018-154100](20261018-154100/status.md) | fixed | maps/E1M2.bsp | got stuck in collision from firing and being pushed into the wall |
| [20261018-154121](20261018-154121/status.md) | fixed | maps/E1M2.bsp | sprite is not rotating with sector |
| [20261018-154153](20261018-154153/status.md) | fixed | maps/E1M2.bsp | sprites looks to dark |
| [20261018-154403](20261018-154403/status.md) | fixed | maps/E1M2.bsp | I opened the secret but the collision is preventing me from getting the healthpack |
| [20261018-154455](20261018-154455/status.md) | fixed | maps/E1M2.bsp | part of train sector is not moving |
| [20261018-154552](20261018-154552/status.md) | fixed | maps/E1M2.bsp | train sector is missing, no collision. same as previous report about not all sectors seems to follow |
| [20261018-154642](20261018-154642/status.md) | fixed | maps/E1M2.bsp | collision still exist after blowing up the ground here |
| [20261018-161128](20261018-161128/status.md) | fixed | maps/E1M2.bsp | can't blow up this wall |
| [20261018-162517](20261018-162517/status.md) | fixed | maps/E1M2.bsp | its should be dark inside this train cart? |
| [20261018-162612](20261018-162612/status.md) | fixed | maps/E1M2.bsp | sprite looks to dark |
| [20261018-162718](20261018-162718/status.md) | fixed | maps/E1M2.bsp | sprite on button is too bright |
| [20261018-212040](20261018-212040/status.md) | fixed | maps/E1M2.bsp | should this hallway be this dark? |
| [20261018-213410](20261018-213410/status.md) | fixed | maps/E1M1.bsp | voxel version of the fence/gate is upside down |
| [20261018-220309](20261018-220309/status.md) | fixed | maps/E1M2.bsp | door on this side should also follow the pulasating light |
| [20261018-221630](20261018-221630/status.md) | fixed | maps/E1M2.bsp | UI is not aligned correctly, red, blue, green bars etc and keys |
| [20261018-221701](20261018-221701/status.md) | fixed | maps/E1M2.bsp | UI is not aligned correctly, red, blue, green bar here as well |
| [20261018-221903](20261018-221903/status.md) | fixed | maps/E1M2.bsp | dynamite is not rotating with sector, also one dynamite fell thru the ground |
| [20261019-091142](20261019-091142/status.md) | fixed | maps/e1m1.map | portal is not rendering correctly |
| [20261019-091157](20261019-091157/status.md) | fixed | maps/e1m1.map | no keys to change hud size |
| [20261019-091242](20261019-091242/status.md) | fixed | maps/e1m1.map | voxel/sprites are not being rendered |
| [20261019-091349](20261019-091349/status.md) | fixed | maps/e1m1.map | sprites should be double sided, but can only be seen from one side. Or transparent wall that is here? |
| [20261019-091425](20261019-091425/status.md) | fixed | maps/e1m1.map | another portal bug where it does not align correctly |
| [20261019-091501](20261019-091501/status.md) | fixed | maps/e1m1.map | sector is all dark |
| [20261019-102445](20261019-102445/status.md) | fixed | maps/e1m1.map | portal rendering is strange when getting close to the portal when jumping for a frame or so, just as camera is between I guess? |
| [20261019-104046](20261019-104046/status.md) | fixed | maps/e1m1.map | missing sprite when viewed thru portal |
| [20261019-104148](20261019-104148/status.md) | fixed | maps/e1m1.map | z-fighting doors. looks like the the duplicate open doors remain in the map. Same issue we had in darkplaces a while back. |
| [20261019-104238](20261019-104238/status.md) | fixed | maps/e1m1.map | framerate issue/stuttering when shooting and looking. Check recording. |
| [20261019-104330](20261019-104330/status.md) | fixed | maps/e1m1.map | secondary fire on weapon causing massive stutter |
| [20261019-113058](20261019-113058/status.md) | fixed | maps/e1m1.map | curtains are not rendered correctly |
| [20261019-113150](20261019-113150/status.md) | fixed | maps/e1m1.map | unable to destory lights, should make the sector darker |
| [20261019-113857](20261019-113857/status.md) | fixed | maps/e1m1.map | looks like there is a missing texture here on the side, we are seeing the wall behind it. Difficult to see this since they are using the same texture. |
| [20261019-122623](20261019-122623/status.md) | fixed | maps/e1m1.map | standing and jumping on grave puts camers out of bounds? |
| [20261019-122718](20261019-122718/status.md) | fixed | maps/e1m1.map | sector light don't go out when destroying lights |
| [20261019-122850](20261019-122850/status.md) | fixed | maps/e1m1.map | fire is not animating, prerender textures like in darkplaces or convert it to an actual shader. Check NBlood how it's originally done |
| [20261019-122928](20261019-122928/status.md) | fixed | maps/e1m1.map | sprites appearing and disappearing based on distance |
| [20261019-122949](20261019-122949/status.md) | fixed | maps/e1m1.map | missing transparency on windows |
| [20261019-123034](20261019-123034/status.md) | fixed | maps/e1m1.map | fullscreen effects when taking damage/ picking up items does not take up the full screen but only part of it |
| [20261019-130147](20261019-130147/status.md) | fixed | maps/e1m1.map | there should be an increase in brightness on the ground when the lid opens |
| [20261019-130320](20261019-130320/status.md) | fixed | maps/e1m1.map | game is overall to bright, or the contrast between light and dark areas is not very visible |
| [20261019-131556](20261019-131556/status.md) | fixed | maps/e1m2.map | text is not aligned correctly at the top saying Episodes |
| [20261019-143458](20261019-143458/status.md) | fixed | maps/e1m1.map | small strange geo in the sky here |
| [20261019-143701](20261019-143701/status.md) | fixed | maps/e1m1.map | is this shelf to bright? |
| [20261019-143800](20261019-143800/status.md) | fixed | maps/e1m2.map | ground to bright? |
| [20261019-144017](20261019-144017/status.md) | fixed | maps/e1m2.map | this wall should not be flashing |
| [20261019-162358](20261019-162358/status.md) | fixed | maps/e1m1.map | small geo bug is still here |
| [20261019-162448](20261019-162448/status.md) | fixed | maps/e1m1.map | light is not replicated through portal |
| [20261019-162631](20261019-162631/status.md) | fixed | maps/e1m1.map | sprite shift offset when rendering through portal |
| [20261019-181609](20261019-181609/status.md) | fixed | maps/e1m1.map | lights should not be active until the sprite itself is active, and destroyed if the sprite is destroyed |
| [20261019-181718](20261019-181718/status.md) | fixed | maps/e1m1.map | no shadows even when shadow is turned on. I don't expect to see the light go thru the wall like this |
| [20261019-181857](20261019-181857/status.md) | fixed | maps/e1m1.map | flickering looks strange, i like the idea of clickering but its not correct |
| [20261019-181932](20261019-181932/status.md) | fixed | maps/e1m1.map | dynamic lights is on even when the light switch is off |
| [20261019-182027](20261019-182027/status.md) | fixed | maps/e1m1.map | not sure where this dynamic ligt is coming from |
| [20261019-184126](20261019-184126/status.md) | fixed | maps/e1m2.map | performance issues due to dynamic lights even when I cant see any? |
| [20261019-190046](20261019-190046/status.md) | fixed | maps/e1m3.map | duplicate doors and missing? |
| [20261019-190113](20261019-190113/status.md) | fixed | maps/e1m3.map | opening this vent caude massive stutter |
| [20261019-190242](20261019-190242/status.md) | fixed | maps/e1m3.map | missing ceiling |
| [20261019-190259](20261019-190259/status.md) | fixed | maps/e1m3.map | looks incorrect |
| [20261019-190325](20261019-190325/status.md) | fixed | maps/e1m3.map | fog has collision |
| [20261019-190353](20261019-190353/status.md) | fixed | maps/e1m3.map | missing floor, same as missing ceiling below? |
| [20261019-190441](20261019-190441/status.md) | fixed | maps/e1m3.map | where are all the enemies? |
| [20261019-205912](20261019-205912/status.md) | fixed | maps/e1m3.map | performance issue looking this way |
| [20261019-210003](20261019-210003/status.md) | fixed | maps/e1m3.map | strange geo bug |
| [20261019-210045](20261019-210045/status.md) | fixed | maps/e1m3.map | can see things thru the ceiling |
| [20261019-210314](20261019-210314/status.md) | fixed | maps/e1m3.map | duplication of the lift exist preventing access to top |
| [20261019-210430](20261019-210430/status.md) | fixed | maps/e1m3.map | this room over room is still not fixed, |
| [20261020-101619](20261020-101619/status.md) | fixed | maps/e1m1.map | lets add an option to turn off the auto aim on player weapon, |
| [20261020-101750](20261020-101750/status.md) | fixed | maps/e1m2.map | still seeing performance issues this with dynamic lights off |
| [20261020-101910](20261020-101910/status.md) | fixed | maps/e1m2.map | strange shading on this |
| [20261020-102145](20261020-102145/status.md) | fixed | maps/e1m3.map | portal still messed up here |
| [20261020-105925](20261020-105925/status.md) | fixed | maps/e1m4.map | missign underwater filter? |
| [20261020-105949](20261020-105949/status.md) | fixed | maps/e1m4.map | how do I swim up and down? |
| [20261020-161118](20261020-161118/status.md) | fixed | maps/e1m3.map | slight whole in geo |
| [20261020-172932](20261020-172932/status.md) | fixed | maps/e1m4.map | missing collision |
| [20261020-173031](20261020-173031/status.md) | fixed | maps/e1m4.map | some massive spikes one just happend before going into this room |
| [20261020-173048](20261020-173048/status.md) | fixed | maps/e1m4.map | massive performance issue here |
| [20261020-231336](20261020-231336/status.md) | fixed | maps/e1m7.map | looks like irst projectile from the tesla gun is incorrectly sideways |
| [20261020-231412](20261020-231412/status.md) | fixed | maps/e1m7.map | killed the boss but level is not finished |
| [20261020-231435](20261020-231435/status.md) | fixed | maps/e1m7.map | think boss don't have correct hp, died very fast |
| [20261020-231602](20261020-231602/status.md) | fixed | maps/e1m6.map | something is not correct with this geo, room in room issue? |
| [20261020-231711](20261020-231711/status.md) | fixed | maps/e1m6.map | more performance issues |
| [20261020-231817](20261020-231817/status.md) | fixed | maps/e1m6.map | very bad performance |
| [20261020-231956](20261020-231956/status.md) | fixed | maps/e1m4.map | flashing wall |
| [20261020-232025](20261020-232025/status.md) | fixed | maps/e1m4.map | very bad performance |
| [20261021-083036](20261021-083036/status.md) | fixed | maps/e1m8.map | this does not look correct? Compare to NBlood |
| [20261021-083322](20261021-083322/status.md) | fixed | maps/e1m4.map | should be able to shoot through this window, but prevent player collision |
| [20261021-083436](20261021-083436/status.md) | fixed | maps/e1m4.map | using voodoo doll without enemies nearby should damage player instead |
| [20261021-083512](20261021-083512/status.md) | fixed | maps/e1m4.map | voodoo doll secondary fire does nothing? |
| [20261021-083656](20261021-083656/status.md) | fixed | maps/e1m4.map | look over secondary fire of the staff weapon, it seems to also don't do anythign |
| [20261021-084032](20261021-084032/status.md) | fixed | maps/e1m6.map | some more geo issues here, room in room? |
| [20261021-194744](20261021-194744/status.md) | fixed | maps/e2m2.map | missing sides? |
| [20261021-200745](20261021-200745/status.md) | fixed | maps/e2m2.map | differen swimming speeds here and next to it, I make anothe report |
| [20261021-200748](20261021-200748/status.md) | fixed | maps/e2m2.map | differen swimming speeds here and next to it, I make anothe report |
| [20261021-200826](20261021-200826/status.md) | fixed | maps/e2m2.map | looks strange between the areas and also having issues jumping out of the water |
| [20261021-204555](20261021-204555/status.md) | fixed | maps/e2m2.map | when waterlevel is increasd this sector needs to go with it when inside water. looks fine from above the water |
| [20261021-204702](20261021-204702/status.md) | fixed | maps/e2m2.map | ehh.. it looks fine now. Not sure why it changed when jumping out of water and in again |
| [20261021-204849](20261021-204849/status.md) | fixed | maps/e2m2.map | same as bug I reported before about missing sides, it looks like the gate here is missing a small piece of the wall |
| [20261022-083332](20261022-083332/status.md) | fixed | maps/e2m9.map | missing swimming volume here |
| [20261022-103239](20261022-103239/status.md) | fixed | maps/e2m5.map | bookcause works creat just this sprite is following it along with things on the floor, will compare to NBlood but the portrait is strange |
| [20261022-122328](20261022-122328/status.md) | fixed | maps/e1m1.map | here is where I saw the AO in the sky |
| [20261023-083949](20261023-083949/status.md) | fixed | maps/e2m6.map | iceblock should be bobing before stepping on it |
| [20261023-084101](20261023-084101/status.md) | fixed | maps/e2m6.map | the moving sheet does not look correct when it moves to the side. In NBlood it looks like it's moving in sync with the tilting? |
| [20261023-090516](20261023-090516/status.md) | fixed | maps/e2m6.map | can fall in water before the sector is moved out of the way |
| [20261023-090601](20261023-090601/status.md) | fixed | maps/e2m6.map | in NBlood the textures strech to keep the textures in sync and no visible seams when it moves out of the way |
| [20261023-090620](20261023-090620/status.md) | fixed | maps/e2m6.map | whole in geo after iceblock moves |
| [20261023-090634](20261023-090634/status.md) | fixed | maps/e2m6.map | whole in geo after iceblock move? |
| [20261023-094911](20261023-094911/status.md) | fixed | maps/e2m6.map | more geo wholes |
| [20261023-095006](20261023-095006/status.md) | fixed | maps/e2m6.map | more strange geo buga when iceblock is on the move |
| [20261023-095131](20261023-095131/status.md) | fixed | maps/e2m6.map | bobbing of this part seems to be clamped and does not follow the rest of the sec down, but works fine on the way up |
| [20261023-114350](20261023-114350/status.md) | fixed | maps/e1m1.map | falling down the grave on start, can't get out |
| [20261023-114439](20261023-114439/status.md) | fixed | maps/e1m1.map | missing geo on door sides |
| [20261023-114451](20261023-114451/status.md) | fixed | maps/e1m1.map | door won't open |
| [20261023-114614](20261023-114614/status.md) | fixed | maps/e1m1.map | curtains wont open |
| [20261023-114653](20261023-114653/status.md) | fixed | maps/e1m1.map | missing geo when shelf is open |
| [20261023-123124](20261023-123124/status.md) | fixed | maps/e1m1.map | lid looks like just a plane, should look like a box |
| [20261023-123446](20261023-123446/status.md) | fixed | maps/e1m2.map | messed up door |
| [20261023-123507](20261023-123507/status.md) | fixed | maps/e1m2.map | can open but collision is still there |
| [20261023-125743](20261023-125743/status.md) | fixed | maps/e1m1.map | side textures here should be stretching with the coffin lid opens. |
| [20261023-135907](20261023-135907/status.md) | fixed | maps/e2m6.map | strange sides herew |
| [20261023-135919](20261023-135919/status.md) | fixed | maps/e2m6.map | invisible collision infront of me here |
| [20261023-135942](20261023-135942/status.md) | fixed | maps/e2m6.map | difficult going into the water |
| [20261023-145810](20261023-145810/status.md) | fixed | maps/e2m6.map | still invisible collision here after iceblock |
| [20261023-145852](20261023-145852/status.md) | fixed | maps/e2m6.map | fell out of bounds |
| [20261023-171150](20261023-171150/status.md) | fixed | maps/e2m6.map | collicion is really wonky around the tapering edges when getting out of the water |
| [20261023-171545](20261023-171545/status.md) | fixed | maps/e1m2.map | slight issue with this wall not changing its light when doors to closet opens |
| [20261023-171702](20261023-171702/status.md) | fixed | maps/e1m2.map | gates open but collision is still there |
| [20261023-171745](20261023-171745/status.md) | fixed | maps/e1m2.map | ignore previous bug report issues is with collision on the train I cant get on it |
| [20261023-171814](20261023-171814/status.md) | fixed | maps/e1m2.map | looks like there is a ceiling following along with the train causing a strange issue |
| [20261023-181544](20261023-181544/status.md) | fixed | maps/e1m2.map | looks like the tracks following with the train should have a scrolling texture to make it be in sync with the ground around it at all times |
| [20261023-181630](20261023-181630/status.md) | fixed | maps/e1m2.map | still some strange sky geo following along with the train you can see it on the wall behind changing height, it should look to be all the same height |
| [20261023-181659](20261023-181659/status.md) | fixed | maps/e1m1.map | z-fighting texture |
| [20261023-195622](20261023-195622/status.md) | fixed | maps/e1m1.map | here the coffin texture is not following the lid |
| [20261023-200532](20261023-200532/status.md) | fixed | maps/e1m3.map | door hanging down to lower section of train cart |
| [20261023-200544](20261023-200544/status.md) | fixed | maps/e1m3.map | whole in geo |
| [20261023-200647](20261023-200647/status.md) | fixed | maps/e1m3.map | lift platform is not moving just the ceiling? |
| [20261023-200801](20261023-200801/status.md) | fixed | maps/e1m3.map | some incorrect geo at top of closet door |
| [20261023-200849](20261023-200849/status.md) | fixed | maps/e1m3.map | missing part when opening door |
| [20261023-210110](20261023-210110/status.md) | fixed | maps/e1m3.map | textures on walls here, think they are portals you are supposed to see through |
| [20261023-210146](20261023-210146/status.md) | fixed | maps/e1m3.map | duplicate doors here flickering |
| [20261023-224444](20261023-224444/status.md) | fixed | maps/e1m8.map | flowing water ground here seems to push the player the wrong way |
| [20261023-224531](20261023-224531/status.md) | fixed | maps/e1m8.map | diving suit ui visor is missing under water |
| [20261023-224613](20261023-224613/status.md) | fixed | maps/e1m8.map | why is there two keys here? |
| [20261023-224655](20261023-224655/status.md) | fixed | maps/e1m8.map | performance issue at this spot as well |
| [20261023-224829](20261023-224829/status.md) | fixed | maps/e1m3.map | geo has holes |
| [20261023-224833](20261023-224833/status.md) | fixed | maps/e1m3.map | geo has holes |
| [20261023-224920](20261023-224920/status.md) | fixed | maps/e1m3.map | geo has holes in top part as well |
| [20261023-233038](20261023-233038/status.md) | fixed | maps/e1m3.map | small hole/ missing geo when door is open |
| [20261023-233102](20261023-233102/status.md) | fixed | maps/e1m3.map | small hole/ missing geo when curtains are open |
| [20261023-233120](20261023-233120/status.md) | fixed | maps/e1m3.map | small hole/ missing geo when door is open |
| [20261024-095834](20261024-095834/status.md) | fixed | maps/e1m3.map | geo sticking up thru the ground, looks like the closet from down below |
| [20261024-095939](20261024-095939/status.md) | fixed | maps/e1m8.map | player not being push along the correct flow of the river |
| [20261024-100518](20261024-100518/status.md) | fixed | maps/e1m4.map | I'm pretty sure the zombies here should come alive, lets check with NBlood |
| [20261024-101111](20261024-101111/status.md) | fixed | maps/e1m5.map | this wall looks incorrect |
| [20261024-101135](20261024-101135/status.md) | fixed | maps/e1m5.map | crash? level just restarted |
| [20261024-182730](20261024-182730/status.md) | fixed | maps/e2m3.map | slight gap |
| [20261024-182805](20261024-182805/status.md) | fixed | maps/e2m2.map | pickup is seen from shared room |
| [20261024-193401](20261024-193401/status.md) | fixed | maps/e1m5.map | this room is not correct, collision in the way so can't even enter |
| [20261024-223841](20261024-223841/status.md) | fixed | maps/e1m5.map | hole in geo |
| [20261024-223856](20261024-223856/status.md) | fixed | maps/e1m5.map | strange sprite, might be a room in room problem here? |
| [20261025-102657](20261025-102657/status.md) | fixed | maps/e2m5.map | dynamic lights in room in room? |
| [20261025-105901](20261025-105901/status.md) | fixed | maps/e2m5.map | invisible collision here |
| [20261025-160556](20261025-160556/status.md) | fixed | maps/e3m5.map | small hole in geo |
| [20261025-160648](20261025-160648/status.md) | fixed | maps/e3m5.map | teleport not working? |
| [20261025-185658](20261025-185658/status.md) | fixed | maps/e3m5.map | wall with no collision, lift? compare to NBlood |
| [20261025-185719](20261025-185719/status.md) | fixed | maps/e3m5.map | very messed up rendering from inside the lift |
| [20261025-185832](20261025-185832/status.md) | fixed | maps/e3m5.map | no damage on floor here? |
| [20261026-093259](20261026-093259/status.md) | fixed | maps/e1m4.map | carousel is not rotating |
| [20261026-093556](20261026-093556/status.md) | fixed | maps/e1m3.map | some strangeness here with the railing disapearring/appearing when moving |
| [20261026-093820](20261026-093820/status.md) | fixed | maps/e3m5.map | some z-figting on top of the platform, moving the textures don't follow (global mode) |
| [20261026-093911](20261026-093911/status.md) | fixed | maps/e3m5.map | missing geo when the turning door is in motion |
| [20261026-120645](20261026-120645/status.md) | fixed | maps/e3m5.map | platform should be rotating and moving at the same time |
| [20261026-123732](20261026-123732/status.md) | fixed | maps/e3m5.map | sound when standing in lava is repeated to fast, sound wrong |
| [20261026-125330](20261026-125330/status.md) | fixed | maps/e1m2.map | add missing halo to plasma gun projectiles |
| [20261026-125431](20261026-125431/status.md) | fixed | maps/e1m2.map | sound when firing shotgun is cutoff |
| [20261026-130808](20261026-130808/status.md) | fixed | maps/e3m1.map | invisisble collision |
| [20261026-130935](20261026-130935/status.md) | fixed | maps/e3m1.map | I can see the wrong room here, not sure if the collision is correct |
| [20261026-131012](20261026-131012/status.md) | fixed | maps/e3m1.map | geo here looks very wrong |
| [20261026-131047](20261026-131047/status.md) | fixed | maps/e3m1.map | hands are unable to grab the player? |
| [20261026-140737](20261026-140737/status.md) | fixed | maps/e3m2.map | texture is upside down |
| [20261026-141026](20261026-141026/status.md) | fixed | maps/e3m2.map | invisible collision after raising floor, can't progress |
| [20261026-143245](20261026-143245/status.md) | fixed | maps/e3m2.map | fell of of world |
| [20261026-143338](20261026-143338/status.md) | fixed | maps/e3m2.map | sky looks strange |
| [20261026-143500](20261026-143500/status.md) | fixed | maps/e3m2.map | incorrect geo |
| [20261026-154529](20261026-154529/status.md) | fixed | maps/e3m2.map | still geo here that should not be there |
| [20261026-180722](20261026-180722/status.md) | fixed | maps/e3m3.map | some glitches here going down into the water sometimes, like 1 frame the wrong stuff is rendered |
| [20261026-180824](20261026-180824/status.md) | fixed | maps/e3m3.map | missing geo |
| [20261026-180901](20261026-180901/status.md) | fixed | maps/e3m3.map | floating waterplane |
| [20261026-181005](20261026-181005/status.md) | fixed | maps/e3m3.map | does sound travel correctly through portals? |
| [20261026-181057](20261026-181057/status.md) | fixed | maps/e3m3.map | hitting some strange collision here with the napalm |
| [20261026-181208](20261026-181208/status.md) | fixed | maps/e3m3.map | floating geo |
| [20261026-182240](20261026-182240/status.md) | fixed | maps/e3m5.map | this looks incorrect |
| [20261026-182348](20261026-182348/status.md) | fixed | maps/e3m5.map | this looks incorrect |
| [20261026-182526](20261026-182526/status.md) | fixed | maps/e3m5.map | falling down when walking here |
| [20261026-182743](20261026-182743/status.md) | fixed | maps/e3m6.map | getting pushed by the water under this bridge when walking on the bridge |
| [20261026-182803](20261026-182803/status.md) | fixed | maps/e3m6.map | missing lift? |
| [20261026-182832](20261026-182832/status.md) | fixed | maps/e3m6.map | missing geo |
| [20261026-204213](20261026-204213/status.md) | fixed | maps/e3m6.map | still a push at the start of bridge but not on bridge check recording for exact position |
| [20261026-204454](20261026-204454/status.md) | fixed | maps/e3m5.map | some flashing here (changing sector drawing) |
| [20261027-092415](20261027-092415/status.md) | fixed | maps/e3m5.map | collision on moving platform here stopped working |
| [20261027-092618](20261027-092618/status.md) | fixed | maps/e1m3.map | chest here in the wrong position |
| [20261027-092928](20261027-092928/status.md) | fixed | maps/e1m4.map | no collision on door |
| [20261027-092938](20261027-092938/status.md) | fixed | maps/e1m4.map | hole in geo |
| [20261027-093224](20261027-093224/status.md) | fixed | maps/e1m4.map | dont rotate with carousle |
| [20261027-093607](20261027-093607/status.md) | fixed | maps/e1m8.map | can we make pickups always use the sectors current lightning now? |
| [20261027-103605](20261027-103605/status.md) | fixed | maps/e1m3.map | using noclip and flying to the chest location and it looks all messed up |
| [20261027-103612](20261027-103612/status.md) | fixed | maps/e1m3.map | using noclip and flying to the chest location and it looks all messed up |
| [20261027-202000](20261027-202000/status.md) | fixed | maps/e4m1.map | broken window but collision is still there |
| [20261027-202043](20261027-202043/status.md) | fixed | maps/e4m1.map | thunder sound is cery lound and sounds wrong\ |
| [20261027-202142](20261027-202142/status.md) | fixed | maps/e4m1.map | thunder bug sound like the sound is crackling |
| [20261027-202304](20261027-202304/status.md) | fixed | maps/e4m2.map | tiny strip of geo hanging here right where crosshair is |
| [20261027-202333](20261027-202333/status.md) | fixed | maps/e4m2.map | door missing collision |
| [20261027-202414](20261027-202414/status.md) | fixed | maps/e4m2.map | hole in geo |
| [20261027-202447](20261027-202447/status.md) | fixed | maps/e4m2.map | hole in geo after moving |
| [20261027-202540](20261027-202540/status.md) | fixed | maps/e4m2.map | hole in geo most likley due to the previous report |
| [20261027-202741](20261027-202741/status.md) | fixed | maps/e4m2.map | should have collision? |
| [20261027-202920](20261027-202920/status.md) | fixed | maps/e4m3.map | another window collision sill there bug |
| [20261027-203112](20261027-203112/status.md) | fixed | maps/e4m3.map | texture should be scrolling |
| [20261027-203141](20261027-203141/status.md) | fixed | maps/e4m3.map | texture should be scrolling |
| [20261027-203158](20261027-203158/status.md) | fixed | maps/e4m3.map | ignore last bug mistake! |
| [20261027-203545](20261027-203545/status.md) | fixed | maps/e4m4.map | small section here where it should be water |
| [20261028-085030](20261028-085030/status.md) | fixed | maps/e3m3.map | sprite on bridge seems to be moving when camera is moving creating a jitter effect |
| [20261028-085657](20261028-085657/status.md) | fixed | maps/e3m5.map | collision on falling teleport you up on the platform, in NBlood you hit the platform side and fall down into the lave. Here you somehow teleport up on the platform instead |
| [20261028-103255](20261028-103255/status.md) | fixed | maps/e3m3.map | here is the part where flickering happening |
| [20261028-123008](20261028-123008/status.md) | fixed | maps/dwe1m1.map | minor missing hole in geo herwe |
| [20261028-123238](20261028-123238/status.md) | fixed | maps/dwe1m1.map | what are the books for? check with NBlood? |
| [20261028-123426](20261028-123426/status.md) | fixed | maps/dwe1m2.map | should the car coming at you kill you? check with NBlood |
| [20261028-123952](20261028-123952/status.md) | fixed | maps/dwe1m3.map | performance here is not great, look into it |
| [20261028-141737](20261028-141737/status.md) | fixed | maps/dwe1m1.map | see if we can fix performance here when the boat is triggered |
| [20261028-174211](20261028-174211/status.md) | fixed | maps/mrw01.map | missing water surface? |
| [20261028-174309](20261028-174309/status.md) | fixed | maps/mrw01.map | missing wall |
| [20261028-174551](20261028-174551/status.md) | fixed | maps/mrw01.map | some moving object like this door causing some meshes to have holes for 1 frame when moving |
| [20261028-174911](20261028-174911/status.md) | fixed | maps/cp09.map | trees are cutoff, think we need to change how rendering of sky is done. Render Sky first and just don't render anythign at all when a brush has a sky texture on them |
| [20261029-083114](20261029-083114/status.md) | fixed | maps/e1m1.map | sky has this dark areas now that is not supposed to be there |
| [20261029-083736](20261029-083736/status.md) | fixed | maps/dwe1m1.map | incorrect slicing when boat is moving? |
| [20261029-083833](20261029-083833/status.md) | fixed | maps/dwe1m1.map | tree is still cut off in sky |
| [20261029-084251](20261029-084251/status.md) | fixed | maps/dwe3m1.map | missing boat? |
| [20261029-084315](20261029-084315/status.md) | fixed | maps/dwe3m1.map | I think I was supposed to start on a boat here? |
| [20261029-084507](20261029-084507/status.md) | fixed | maps/dwe3m1.map | missing fog/mist here |
| [20261029-094607](20261029-094607/status.md) | fixed | maps/e1m1.map | projectiles like napalm crossing portal produces a strange double projectile seen from the side? |
| [20261029-105538](20261029-105538/status.md) | fixed | maps/dwe3m1.map | when looking this way the trees cut off, but not if I look directly at them |
| [20261029-105619](20261029-105619/status.md) | fixed | maps/dwe3m1.map | got crushed trying to ride the boat |
| [20261029-105717](20261029-105717/status.md) | fixed | maps/dwe3m1.map | incorrect brightness on sprites in the ceiling? |
| [20261029-105758](20261029-105758/status.md) | fixed | maps/dwe3m1.map | lots of flickering geo when riding on boat (immortal or it will kill me) |
| [20261029-105904](20261029-105904/status.md) | fixed | maps/dwe3m1.map | AO looks strange on this level, something we can do to take the fog into consideration when applying the AO? |
| [20261029-105935](20261029-105935/status.md) | fixed | maps/dwe3m1.map | weapon is black, compare to NBlood but looks wrong |
| [20261029-125840](20261029-125840/status.md) | fixed | maps/dwe3m1.map | shore is white when riding boat |
| [20261029-152743](20261029-152743/status.md) | fixed | maps/dwe3m1.map | trees are cutoff here |
| [20261029-153012](20261029-153012/status.md) | fixed | maps/dwe3m1.map | missing ground when opening door |
| [20261029-153119](20261029-153119/status.md) | fixed | maps/dwe3m1.map | is this correct? jumping down the well |
| [20261029-171710](20261029-171710/status.md) | fixed | maps/dwe3m1.map | moving door has some small white dots like the cutting mesh is not perfectly aligned to angle of door |
| [20261029-171922](20261029-171922/status.md) | fixed | maps/dwe3m1.map | fog is thicker or falloff is much lowe than Nblood |
| [20261029-202850](20261029-202850/status.md) | fixed | maps/e1m2.map | missing geo when train comes in, only when moving in some areas |
| [20261029-230637](20261029-230637/status.md) | fixed | maps/dwe3m1.map | look at performance here |
| [20261030-090034](20261030-090034/status.md) | fixed | maps/e1m8.map | some issues here with flickering when rebuilding meshes |
| [20261030-101950](20261030-101950/status.md) | fixed | maps/dwe3m1.map | crouching in boat made me push into water |
| [20261030-113748](20261030-113748/status.md) | fixed | mods/dw/maps/dwe3m1.map | have to spam click to get boat going? |
| [20261030-134403](20261030-134403/status.md) | fixed | maps/e3m3.map | throwing hand into water and sprite got duplicated, the one above water can't be attacked |
| [20261030-134448](20261030-134448/status.md) | fixed | maps/e3m3.map | make it so when flying using command be able to go through portals |
| [20261030-134538](20261030-134538/status.md) | fixed | mods/dw/maps/dwe3m1.map | small edge in water |
| [20261030-145434](20261030-145434/status.md) | fixed | mods/dw/maps/dwe3m1.map | difficulty walk out this door, seem to get stuck on the side for a bit |
| [20261030-145643](20261030-145643/status.md) | fixed | maps/e1m1.map | AO needs to take the light into account? the dark edges looks pretty bad with the fog |
| [20261030-145926](20261030-145926/status.md) | fixed | maps/e1m1.map | sky is affected by fogs light |
| [20261030-155526](20261030-155526/status.md) | fixed | maps/e1m1.map | ground mist is confused here about the portal I think |
| [20261030-171329](20261030-171329/status.md) | fixed | maps/e2m1.map | performance here is terrible |
| [20261030-171348](20261030-171348/status.md) | fixed | maps/e2m1.map | hole in geo herew |
| [20261030-171546](20261030-171546/status.md) | fixed | maps/e1m1.map | fog should be on ground here but its above in the portal if I use fly |
| [20261030-191431](20261030-191431/status.md) | fixed | maps/e2m1.map | hole in geo |
| [20261030-191444](20261030-191444/status.md) | fixed | maps/e2m1.map | invisible collision |
| [20261030-191513](20261030-191513/status.md) | fixed | maps/e2m1.map | performance is terrible while underwater |
| [20261030-191531](20261030-191531/status.md) | fixed | maps/e2m1.map | water in boat, should not be there |
| [20261030-191704](20261030-191704/status.md) | fixed | maps/e1m1.map | changing rain amount or weather amount does nothing while playing, maybe restart work? |
| [20261031-083335](20261031-083335/status.md) | fixed | maps/e1m1.map | fame is seen here when building is not destroyed |
| [20261031-083356](20261031-083356/status.md) | fixed | maps/e1m1.map | can't select episodes in main menu |
| [20261031-083424](20261031-083424/status.md) | fixed | maps/e2m1.map | moving boat is very jittery |
| [20261031-083617](20261031-083617/status.md) | fixed | maps/e1m8.map | flying through portals no longer works |
| [20261031-084013](20261031-084013/status.md) | fixed | maps/e2m6.map | transparent water bug, missing the portal renderer for the other side |
| [20261031-090658](20261031-090658/status.md) | fixed | maps/e1m3.map | incorrect showing of doors |
| [20261031-090743](20261031-090743/status.md) | fixed | maps/e1m3.map | can't break window, and window is missing its collision |
| [20261031-092746](20261031-092746/status.md) | fixed | maps/e1m3.map | human at start got stuck, it should collide with the train |
| [20261031-093142](20261031-093142/status.md) | fixed | maps/e3m1.map | incorrect geo a piece is seen that should not be visible |
| [20261031-113416](20261031-113416/status.md) | fixed | maps/dwe3m1.map | can't start boat |
| [20261031-113433](20261031-113433/status.md) | fixed | maps/dwe3m1.map | gaps in water geo when boat is moving |
| [20261031-113518](20261031-113518/status.md) | fixed | maps/dwe3m1.map | same bug when dooor is moving |
| [20261031-113654](20261031-113654/status.md) | fixed | maps/e1m1.map | hole in geo |
| [20261031-142902](20261031-142902/status.md) | fixed | maps/e3m1.map | ground fog disappearing in some angles |
| [20261031-142950](20261031-142950/status.md) | fixed | maps/e3m1.map | changing weather settings is still somewhat bugged and is either not responding or removing all weather |
| [20261031-143108](20261031-143108/status.md) | fixed | maps/e1m1.map | make rain be affected by wind strength, increase leafes with wind amount |
| [20261031-143141](20261031-143141/status.md) | fixed | maps/e1m1.map | staff dynamic light is green? |
| [20261031-143341](20261031-143341/status.md) | fixed | maps/e1m1.map | hole in geo |
| [20261031-143509](20261031-143509/status.md) | fixed | maps/e2m1.map | standing on edge of boat, player does not move with boat |
| [20261031-155624](20261031-155624/status.md) | fixed | maps/dwe3m1.map | moving while on the boat sort of bounces you back, check against NBlood |
| [20261031-172051](20261031-172051/status.md) | fixed | maps/mrw01.map | flame is very dull, why not bright like a napalm explosion? |
| [20261031-172211](20261031-172211/status.md) | fixed | maps/e2m1.map | napalm sprite get stuck in portal after firing |
| [20261031-220422](20261031-220422/status.md) | fixed | maps/e1m1.map | can we fix so lights behind wallks are not shining through. Double sided geo on shadows maybe? |
| [20261031-220507](20261031-220507/status.md) | fixed | maps/e1m1.map | try make rain look more pixelated and fit better with the style of the game. Slow down the speed of the rain. |
| [20261031-220610](20261031-220610/status.md) | fixed | maps/e1m1.map | fix leafes in wind so they don't spin like crazy, increase the amount. |
| [20261031-220644](20261031-220644/status.md) | fixed | maps/e1m1.map | would be interesting if we could somehow make the fog feel more pixelated to fit better with the style of the game. |
| [20261031-220911](20261031-220911/status.md) | fixed | maps/e1m2.map | train sky sector is making a strange cut in wall/ or renderign is strange here |
| [20261031-230247](20261031-230247/status.md) | fixed | maps/e1m1.map | mist pixel size don't show up until you leave menu and come back |
| [20261031-230439](20261031-230439/status.md) | fixed | maps/e2m1.map | check if there is something we can do about the seams between water an portal when transparent water is enabled. If its to tricky just skip it |
| [20261031-230527](20261031-230527/status.md) | fixed | maps/e2m1.map | secondary fire on napalm is not correct, should be like a grenade |
| [20261031-230623](20261031-230623/status.md) | fixed | maps/e2m1.map | make snow particles slightly pixelated |
| [20261101-082036](20261101-082036/status.md) | fixed | maps/e2m1.map | make the napalm explosion light up a big area, fast in slow out |
| [20261101-121241](20261101-121241/status.md) | fixed | maps/e2m1.map | explosion aftermath looks pretty terrible. Of somthing make it a round distortion around the impact point but very slight distortion |
| [20261101-121307](20261101-121307/status.md) | fixed | maps/e2m1.map | missing hud and weapons under water |
| [20261101-121434](20261101-121434/status.md) | fixed | maps/e2m1.map | AO needs to take underwater fog intro consideration just like normal fog |
| [20261101-121942](20261101-121942/status.md) | fixed | maps/e1m1.map | glowing pixels causing some strange issues on some sprites |
| [20261101-122121](20261101-122121/status.md) | fixed | maps/e1m1.map | moving sky looks mostly buggy |
| [20261101-122328](20261101-122328/status.md) | fixed | maps/e1m1.map | make mist on ground more consistent, vieweing from here it's all gone until I jump down on the ground |
| [20261101-133141](20261101-133141/status.md) | fixed | maps/e1m1.map | sprite getting cut off near edge of screen |
| [20261101-133417](20261101-133417/status.md) | fixed | maps/e1m1.map | remove moving sky |
| [20261101-133626](20261101-133626/status.md) | fixed | maps/e1m1.map | more sprites disappearing at edge of screen |
| [20261101-134617](20261101-134617/status.md) | fixed | maps/e6m1.map | need a sky fix, parts of houses are not visible |
| [20261101-134701](20261101-134701/status.md) | fixed | maps/dwe1m1.map | this level is totally broken now for some reason |
| [20261101-134740](20261101-134740/status.md) | fixed | maps/dwe1m1.map | restarted level and looks ok now. |
| [20261101-135005](20261101-135005/status.md) | fixed | maps/cp01.map | lots of frame stuttering here |
| [20261101-151545](20261101-151545/status.md) | fixed | maps/cp01.map | still stuttering framerate here, look into it |
| [20261101-151653](20261101-151653/status.md) | fixed | maps/e1m1.map | hole in geo here now |
| [20261101-151717](20261101-151717/status.md) | fixed | maps/e1m1.map | light shining through wall is back |
| [20261101-165435](20261101-165435/status.md) | fixed | maps/e1m1.map | another portal bug with dynamite getting a copy of the sprite stuck in air |
| [20261101-170716](20261101-170716/status.md) | fixed | maps/e1m5.map | look into if there is a fix for the bleeding of moonlight? |
| [20261101-191348](20261101-191348/status.md) | fixed | maps/e3m5.map | when flying and outside any sector then show all sectors to make it easier to navigate when flying around |
| [20261101-191449](20261101-191449/status.md) | fixed | maps/e3m5.map | small strip of missing geo here when door is rotating |
| [20261101-191705](20261101-191705/status.md) | fixed | maps/e1m6.map | starting e1m6 and you die instantly, think you are spawning in the wrong sectors since its a room in room |
| [20261101-202830](20261101-202830/status.md) | fixed | maps/e1m2.map | bug happened again, seems to happen when loading a level using command |
| [20261101-211612](20261101-211612/status.md) | fixed | maps/mrw2m1.map | the dynamic light coming from candles looks a bit off |
| [20261101-211729](20261101-211729/status.md) | fixed | maps/e1m1.map | can we make the dynamic light from fire/torches flicker a bit |
| [20261101-211805](20261101-211805/status.md) | fixed | maps/e1m1.map | same here dynamic light is a bit off from the actual lantern |
| [20261101-211859](20261101-211859/status.md) | fixed | maps/e1m1.map | I wonder if there is some way to make the sky blend better when using fog? |
| [20261101-221212](20261101-221212/status.md) | fixed | maps/e2m1.map | not following the iceberg, check with NBlood if we should be when standing on it? Also didn't fall down once the iceberg was gone from under me. |
| [20261102-083959](20261102-083959/status.md) | fixed | maps/e1m1.map | lamps are only half lit, half bright and half dull |
| [20261102-085848](20261102-085848/status.md) | fixed | maps/dwe3m1.map | look into why this big black thing is in the sky |
| [20261102-090029](20261102-090029/status.md) | fixed | maps/dwe3m1.map | napalm going through enemies |
| [20261102-090217](20261102-090217/status.md) | fixed | maps/dwe4m1.map | lots of geo issues on this level |
| [20261102-095335](20261102-095335/status.md) | fixed | maps/dwe2m1.map | look into performance of this level |
| [20261103-125459](20261103-125459/status.md) | fixed | maps/test.map | strange 1 frame here when I move between sectors and the visibility seems to be incorrect causing a fast flicker for 1 frame. |
| [20261104-140040](20261104-140040/status.md) | fixed | maps/test.map | floor texture moving around (jitter) when sector is moving |
| [20261105-095023](20261105-095023/status.md) | fixed | maps/test.map | I fell from wall and ended up in the wrong sector room-over-room issue |
| [20261105-105957](20261105-105957/status.md) | fixed | maps/test.map | fell into other sector again |
| [20261105-143410](20261105-143410/status.md) | fixed | maps/test.map | looks like game can't render two floors over each other. Seems fine in editor |
| [20261105-153923](20261105-153923/status.md) | fixed | maps/test.map | jumped and got stuck in the air |
| [20261105-181541](20261105-181541/status.md) | fixed | maps/test.map | the improved water rendering needs to support water under water links, right now it only shows the water in the sector you currently are |
| [20261105-220910](20261105-220910/status.md) | fixed | maps/test.map | shows incorrect sprite that is in a room over room. Zombie should not be visible in the mirror. |
| [20261115-161220](20261115-161220/status.md) | fixed | maps/sevict.map | sprite on vent looks to be offset |
| [20261115-161414](20261115-161414/status.md) | fixed | maps/sevict.map | black room over room. Rendering not working correctly here |
| [20261115-161600](20261115-161600/status.md) | fixed | maps/sstation.map | check if we can increase performance here |
| [20261115-161749](20261115-161749/status.md) | fixed | maps/sstation.map | add a setting to increase dynamic light range |
| [20261117-111050](20261117-111050/status.md) | fixed | maps/sevict.map | performance issues standing here |
| [20261117-113226](20261117-113226/status.md) | fixed | maps/sevict.map | another spot with performance |
