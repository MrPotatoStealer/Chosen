# Regression: stacked-rooms

Rooms stacked so they overlap in height, without a link: StackedRooms.cs (the walk), SectorMap.FindOverlaps, sprites hidden by their Build sector.

45 bug(s), 45 in the last run. Rerun: `python pipeline/bugs.py regress stacked-rooms` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-215820](../../20261015-215820/status.md) Duplicate door bug is back

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-215820`

[report](../../20261015-215820/screenshot.jpg) [NBlood](../../20261015-215820/nblood.jpg) [baseline](../../20261015-215820/baseline.jpg) [now](../../20261015-215820/regress.jpg)

### [20261015-230252](../../20261015-230252/status.md) strange doors that should not be here?

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-230252`

[report](../../20261015-230252/screenshot.jpg) [NBlood](../../20261015-230252/nblood.jpg) [baseline](../../20261015-230252/baseline.jpg) [now](../../20261015-230252/regress.jpg)

### [20261015-232326](../../20261015-232326/status.md) fire should not be visible before blowing up the funeral home

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261015-232326`

[report](../../20261015-232326/screenshot.jpg) [NBlood](../../20261015-232326/nblood.jpg) [baseline](../../20261015-232326/baseline.jpg) [now](../../20261015-232326/regress.jpg)

### [20261015-232356](../../20261015-232356/status.md) Not sure what this gray thing is but it should not be here

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-232356`

[report](../../20261015-232356/screenshot.jpg) [NBlood](../../20261015-232356/nblood.jpg) [baseline](../../20261015-232356/baseline.jpg) [now](../../20261015-232356/regress.jpg)

### [20261019-091425](../../20261019-091425/status.md) another portal bug where it does not align correctly

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261019-091425`

[report](../../20261019-091425/screenshot.jpg) [NBlood](../../20261019-091425/nblood.jpg) [baseline](../../20261019-091425/baseline.jpg) [now](../../20261019-091425/regress.jpg)

### [20261019-190242](../../20261019-190242/status.md) missing ceiling

e1m3, same 2026-11-15 17:14 (0.01). `gotobug 20261019-190242`

[report](../../20261019-190242/screenshot.jpg) [NBlood](../../20261019-190242/nblood.jpg) [baseline](../../20261019-190242/baseline.jpg) [now](../../20261019-190242/regress.jpg)

### [20261019-190259](../../20261019-190259/status.md) looks incorrect

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-190259`

[report](../../20261019-190259/screenshot.jpg) [NBlood](../../20261019-190259/nblood.jpg) [baseline](../../20261019-190259/baseline.jpg) [now](../../20261019-190259/regress.jpg)

### [20261019-190353](../../20261019-190353/status.md) missing floor, same as missing ceiling below?

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-190353`

[report](../../20261019-190353/screenshot.jpg) [NBlood](../../20261019-190353/nblood.jpg) [baseline](../../20261019-190353/baseline.jpg) [now](../../20261019-190353/regress.jpg)

### [20261019-210045](../../20261019-210045/status.md) can see things thru the ceiling

e1m3, same 2026-11-15 17:14 (0.01). `gotobug 20261019-210045`

[report](../../20261019-210045/screenshot.jpg) [NBlood](../../20261019-210045/nblood.jpg) [baseline](../../20261019-210045/baseline.jpg) [now](../../20261019-210045/regress.jpg)

### [20261019-210430](../../20261019-210430/status.md) this room over room is still not fixed,

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-210430`, setup `nomonsters | 0.2`

[report](../../20261019-210430/screenshot.jpg) [NBlood](../../20261019-210430/nblood.jpg) [baseline](../../20261019-210430/baseline.jpg) [now](../../20261019-210430/regress.jpg)

### [20261020-102145](../../20261020-102145/status.md) portal still messed up here

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261020-102145`

[report](../../20261020-102145/screenshot.jpg) [NBlood](../../20261020-102145/nblood.jpg) [baseline](../../20261020-102145/baseline.jpg) [now](../../20261020-102145/regress.jpg)

### [20261020-161118](../../20261020-161118/status.md) slight whole in geo

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261020-161118`, setup `nomonsters | 0.2`

[report](../../20261020-161118/screenshot.jpg) [NBlood](../../20261020-161118/nblood.jpg) [baseline](../../20261020-161118/baseline.jpg) [now](../../20261020-161118/regress.jpg)

### [20261020-231602](../../20261020-231602/status.md) something is not correct with this geo, room in room issue?

e1m6, same 2026-11-15 17:14 (0.08). `gotobug 20261020-231602`

[report](../../20261020-231602/screenshot.jpg) [NBlood](../../20261020-231602/nblood.jpg) [baseline](../../20261020-231602/baseline.jpg) [now](../../20261020-231602/regress.jpg)

### [20261021-084032](../../20261021-084032/status.md) some more geo issues here, room in room?

e1m6, same 2026-11-15 17:14 (0.08). `gotobug 20261021-084032`

[report](../../20261021-084032/screenshot.jpg) [NBlood](../../20261021-084032/nblood.jpg) [baseline](../../20261021-084032/baseline.jpg) [now](../../20261021-084032/regress.jpg)

### [20261023-200532](../../20261023-200532/status.md) door hanging down to lower section of train cart

e1m3, same 2026-11-15 17:14 (0.01). `gotobug 20261023-200532`. Check: no wall of the upper corridor's sliding door hangs from the train car's ceiling

[report](../../20261023-200532/screenshot.jpg) [NBlood](../../20261023-200532/nblood.jpg) [baseline](../../20261023-200532/baseline.jpg) [now](../../20261023-200532/regress.jpg)

### [20261023-200544](../../20261023-200544/status.md) whole in geo

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-200544`. Check: the train car's wall tops run straight, no beam or stone poking through

[report](../../20261023-200544/screenshot.jpg) [NBlood](../../20261023-200544/nblood.jpg) [baseline](../../20261023-200544/baseline.jpg) [now](../../20261023-200544/regress.jpg)

### [20261023-200801](../../20261023-200801/status.md) some incorrect geo at top of closet door

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-200801`. Check: no beams across the ceiling or wooden wall beside the louvered closet door

[report](../../20261023-200801/screenshot.jpg) [NBlood](../../20261023-200801/nblood.jpg) [baseline](../../20261023-200801/baseline.jpg) [now](../../20261023-200801/regress.jpg)

### [20261023-210146](../../20261023-210146/status.md) duplicate doors here flickering

e1m3, same 2026-11-15 17:14 (0.06). `gotobug 20261023-210146`, setup `send 102 | 2`. Check: both doors open: no second door face flickering in the doorway

[report](../../20261023-210146/screenshot.jpg) [NBlood](../../20261023-210146/nblood.jpg) [baseline](../../20261023-210146/baseline.jpg) [now](../../20261023-210146/regress.jpg)

### [20261023-224829](../../20261023-224829/status.md) geo has holes

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-224829`

[report](../../20261023-224829/screenshot.jpg) [NBlood](../../20261023-224829/nblood.jpg) [baseline](../../20261023-224829/baseline.jpg) [now](../../20261023-224829/regress.jpg)

### [20261023-224833](../../20261023-224833/status.md) geo has holes

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-224833`

[report](../../20261023-224833/screenshot.jpg) [NBlood](../../20261023-224833/nblood.jpg) [baseline](../../20261023-224833/baseline.jpg) [now](../../20261023-224833/regress.jpg)

### [20261023-224920](../../20261023-224920/status.md) geo has holes in top part as well

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-224920`

[report](../../20261023-224920/screenshot.jpg) [NBlood](../../20261023-224920/nblood.jpg) [baseline](../../20261023-224920/baseline.jpg) [now](../../20261023-224920/regress.jpg)

### [20261023-233038](../../20261023-233038/status.md) small hole/ missing geo when door is open

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-233038`

[report](../../20261023-233038/screenshot.jpg) [baseline](../../20261023-233038/baseline.jpg) [now](../../20261023-233038/regress.jpg)

### [20261023-233120](../../20261023-233120/status.md) small hole/ missing geo when door is open

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-233120`

[report](../../20261023-233120/screenshot.jpg) [baseline](../../20261023-233120/baseline.jpg) [now](../../20261023-233120/regress.jpg)

### [20261024-095834](../../20261024-095834/status.md) geo sticking up thru the ground, looks like the closet from down below

e1m3, same 2026-11-15 17:14 (0.03). `gotobug 20261024-095834`

[report](../../20261024-095834/screenshot.jpg) [NBlood](../../20261024-095834/nblood.jpg) [baseline](../../20261024-095834/baseline.jpg) [now](../../20261024-095834/regress.jpg)

### [20261024-182805](../../20261024-182805/status.md) pickup is seen from shared room

e2m2, same 2026-11-15 17:14 (0.01). `gotobug 20261024-182805`, setup `nomonsters | 2`. Check: the life seed in the room behind the wall does not show at the wall's corner

[report](../../20261024-182805/screenshot.jpg) [NBlood](../../20261024-182805/nblood.jpg) [baseline](../../20261024-182805/baseline.jpg) [now](../../20261024-182805/regress.jpg)

### [20261024-223841](../../20261024-223841/status.md) hole in geo

e1m5, same 2026-11-15 17:14 (0.00). `gotobug 20261024-223841`

[report](../../20261024-223841/screenshot.jpg) [NBlood](../../20261024-223841/nblood.jpg) [baseline](../../20261024-223841/baseline.jpg) [now](../../20261024-223841/regress.jpg)

### [20261024-223856](../../20261024-223856/status.md) strange sprite, might be a room in room problem here?

e1m5, same 2026-11-15 17:14 (0.01). `gotobug 20261024-223856`

[report](../../20261024-223856/screenshot.jpg) [NBlood](../../20261024-223856/nblood.jpg) [baseline](../../20261024-223856/baseline.jpg) [now](../../20261024-223856/regress.jpg)

### [20261025-102657](../../20261025-102657/status.md) dynamic lights in room in room?

e2m5, same 2026-11-15 17:14 (0.01). `gotobug 20261025-102657`

[report](../../20261025-102657/screenshot.jpg) [NBlood](../../20261025-102657/nblood.jpg) [baseline](../../20261025-102657/baseline.jpg) [now](../../20261025-102657/regress.jpg)

### [20261026-093820](../../20261026-093820/status.md) some z-figting on top of the platform, moving the textures don't follow (global mode)

e3m5, same 2026-11-15 17:14 (0.02). `gotobug 20261026-093820`, setup `nomonsters | 0`. Check: pillar tops one texture, no z-fighting; moving, the texture moves with the pillar

[report](../../20261026-093820/screenshot.jpg) [NBlood](../../20261026-093820/nblood.jpg) [baseline](../../20261026-093820/baseline.jpg) [now](../../20261026-093820/regress.jpg)

### [20261026-130935](../../20261026-130935/status.md) I can see the wrong room here, not sure if the collision is correct

e3m1, same 2026-11-15 17:14 (0.01). `gotobug 20261026-130935`, setup `nomonsters | 0`

[report](../../20261026-130935/screenshot.jpg) [NBlood](../../20261026-130935/nblood.jpg) [baseline](../../20261026-130935/baseline.jpg) [now](../../20261026-130935/regress.jpg)

### [20261026-143338](../../20261026-143338/status.md) sky looks strange

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-143338`, setup `nomonsters | 0`

[report](../../20261026-143338/screenshot.jpg) [NBlood](../../20261026-143338/nblood.jpg) [baseline](../../20261026-143338/baseline.jpg) [now](../../20261026-143338/regress.jpg)

### [20261026-143500](../../20261026-143500/status.md) incorrect geo

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-143500`, setup `nomonsters | 0`

[report](../../20261026-143500/screenshot.jpg) [NBlood](../../20261026-143500/nblood.jpg) [baseline](../../20261026-143500/baseline.jpg) [now](../../20261026-143500/regress.jpg)

### [20261026-181208](../../20261026-181208/status.md) floating geo

e3m3, same 2026-11-15 17:14 (0.00). `gotobug 20261026-181208`

[report](../../20261026-181208/screenshot.jpg) [NBlood](../../20261026-181208/nblood.jpg) [baseline](../../20261026-181208/baseline.jpg) [now](../../20261026-181208/regress.jpg)

### [20261026-182240](../../20261026-182240/status.md) this looks incorrect

e3m5, same 2026-11-15 17:14 (0.00). `gotobug 20261026-182240`

[report](../../20261026-182240/screenshot.jpg) [NBlood](../../20261026-182240/nblood.jpg) [baseline](../../20261026-182240/baseline.jpg) [now](../../20261026-182240/regress.jpg)

### [20261027-202304](../../20261027-202304/status.md) tiny strip of geo hanging here right where crosshair is

e4m2, same 2026-11-15 17:14 (0.00). `gotobug 20261027-202304`

[report](../../20261027-202304/screenshot.jpg) [NBlood](../../20261027-202304/nblood.jpg) [baseline](../../20261027-202304/baseline.jpg) [now](../../20261027-202304/regress.jpg)

### [20261028-123008](../../20261028-123008/status.md) minor missing hole in geo herwe

dwe1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261028-123008`

[report](../../20261028-123008/screenshot.jpg) [NBlood](../../20261028-123008/nblood.jpg) [baseline](../../20261028-123008/baseline.jpg) [now](../../20261028-123008/regress.jpg)

### [20261029-125840](../../20261029-125840/status.md) shore is white when riding boat

dwe3m1, same 2026-11-15 17:14 (0.15). `gotobug 20261029-125840`

[report](../../20261029-125840/screenshot.jpg) [NBlood](../../20261029-125840/nblood.jpg) [baseline](../../20261029-125840/baseline.jpg) [now](../../20261029-125840/regress.jpg)

### [20261031-090658](../../20261031-090658/status.md) incorrect showing of doors

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261031-090658`. Check: no door picture in room 31's ceiling (sliding door 159 of the room above)

[report](../../20261031-090658/screenshot.jpg) [NBlood](../../20261031-090658/nblood.jpg) [baseline](../../20261031-090658/baseline.jpg) [now](../../20261031-090658/regress.jpg)

### [20261031-093142](../../20261031-093142/status.md) incorrect geo a piece is seen that should not be visible

e3m1, same 2026-11-15 17:14 (0.00). `gotobug 20261031-093142`

[report](../../20261031-093142/screenshot.jpg) [NBlood](../../20261031-093142/nblood.jpg) [baseline](../../20261031-093142/baseline.jpg) [now](../../20261031-093142/regress.jpg)

### [20261031-113654](../../20261031-113654/status.md) hole in geo

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261031-113654`

[report](../../20261031-113654/screenshot.jpg) [NBlood](../../20261031-113654/nblood.jpg) [baseline](../../20261031-113654/baseline.jpg) [now](../../20261031-113654/regress.jpg)

### [20261031-143341](../../20261031-143341/status.md) hole in geo

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261031-143341`

[report](../../20261031-143341/screenshot.jpg) [NBlood](../../20261031-143341/nblood.jpg) [baseline](../../20261031-143341/baseline.jpg) [now](../../20261031-143341/regress.jpg)

### [20261102-085848](../../20261102-085848/status.md) look into why this big black thing is in the sky

dwe3m1, same 2026-11-15 17:14 (0.00). `gotobug 20261102-085848`, setup `cam -82.9 -754.3 -452.9 358.8 -22.5 | 1`. Check: no black panel with a white tree over the hedge: sky and trees as in NBlood

[report](../../20261102-085848/screenshot.jpg) [NBlood](../../20261102-085848/nblood.jpg) [baseline](../../20261102-085848/baseline.jpg) [now](../../20261102-085848/regress.jpg)

### [20261102-090217](../../20261102-090217/status.md) lots of geo issues on this level

dwe4m1, same 2026-11-15 17:14 (0.00). `gotobug 20261102-090217`

[report](../../20261102-090217/screenshot.jpg) [NBlood](../../20261102-090217/nblood.jpg) [baseline](../../20261102-090217/baseline.jpg) [now](../../20261102-090217/regress.jpg)

### [20261105-220910](../../20261105-220910/status.md) shows incorrect sprite that is in a room over room. Zombie should not be visible in the mirror.

test, same 2026-11-15 17:14 (0.03). `gotobug 20261105-220910`

[report](../../20261105-220910/screenshot.jpg) [baseline](../../20261105-220910/baseline.jpg) [now](../../20261105-220910/regress.jpg)

### [20261115-161414](../../20261115-161414/status.md) black room over room. Rendering not working correctly here

sevict, same 2026-11-15 17:14 (0.00). `gotobug 20261115-161414`, setup `cam 1569.45 -1943.50 261.94 328.89 17.79 | 2`. Check: the street below shows through the balcony's floor

[report](../../20261115-161414/screenshot.jpg) [NBlood](../../20261115-161414/nblood.jpg) [baseline](../../20261115-161414/baseline.jpg) [now](../../20261115-161414/regress.jpg)

