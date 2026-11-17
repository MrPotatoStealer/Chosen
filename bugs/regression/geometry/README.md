# Regression: geometry

The native world mesh (Render.Native.WorldMesher, WorldShader): holes, cracks and seams, walls between sloped planes, texture pegging, sky.

49 bug(s), 49 in the last run. Rerun: `python pipeline/bugs.py regress geometry` ([sheet.jpg](sheet.jpg): report, NBlood, baseline, now).

### [20261015-120214](../../20261015-120214/status.md) wall have incorrect texture or missing, Its solid but should the the same as the walls next to it.

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-120214`

[report](../../20261015-120214/screenshot.jpg) [NBlood](../../20261015-120214/nblood.jpg) [baseline](../../20261015-120214/baseline.jpg) [now](../../20261015-120214/regress.jpg)

### [20261015-120249](../../20261015-120249/status.md) wall is missing texture, I can see through the wall. Collision seems to be correct

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-120249`

[report](../../20261015-120249/screenshot.jpg) [NBlood](../../20261015-120249/nblood.jpg) [baseline](../../20261015-120249/baseline.jpg) [now](../../20261015-120249/regress.jpg)

### [20261015-152927](../../20261015-152927/status.md) Missing texture, can see to the other side, should be the same as the walls next to it.

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261015-152927`

[report](../../20261015-152927/screenshot.jpg) [NBlood](../../20261015-152927/nblood.jpg) [baseline](../../20261015-152927/baseline.jpg) [now](../../20261015-152927/regress.jpg)

### [20261015-153113](../../20261015-153113/status.md) whole in map? misaligned vertices?

e1m1, same 2026-11-15 17:14 (0.03). `gotobug 20261015-153113`

[report](../../20261015-153113/screenshot.jpg) [NBlood](../../20261015-153113/nblood.jpg) [baseline](../../20261015-153113/baseline.jpg) [now](../../20261015-153113/regress.jpg)

### [20261015-153138](../../20261015-153138/status.md) whole in map, missing texture

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-153138`

[report](../../20261015-153138/screenshot.jpg) [NBlood](../../20261015-153138/nblood.jpg) [baseline](../../20261015-153138/baseline.jpg) [now](../../20261015-153138/regress.jpg)

### [20261015-155021](../../20261015-155021/status.md) still a small hole here in the geometry, it should be filled

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261015-155021`

[report](../../20261015-155021/screenshot.jpg) [NBlood](../../20261015-155021/nblood.jpg) [baseline](../../20261015-155021/baseline.jpg) [now](../../20261015-155021/regress.jpg)

### [20261015-155049](../../20261015-155049/status.md) incorrect texture, should be same as the walls next to it

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-155049`

[report](../../20261015-155049/screenshot.jpg) [NBlood](../../20261015-155049/nblood.jpg) [baseline](../../20261015-155049/baseline.jpg) [now](../../20261015-155049/regress.jpg)

### [20261015-161128](../../20261015-161128/status.md) strange geometry sticking out from wall, it should not bo visible

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-161128`

[report](../../20261015-161128/screenshot.jpg) [NBlood](../../20261015-161128/nblood.jpg) [baseline](../../20261015-161128/baseline.jpg) [now](../../20261015-161128/regress.jpg)

### [20261015-161312](../../20261015-161312/status.md) skewed gateway for some reason, I guess misaligment between two room over room that you are trying to match up but incorrectly

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261015-161312`

[report](../../20261015-161312/screenshot.jpg) [NBlood](../../20261015-161312/nblood.jpg) [baseline](../../20261015-161312/baseline.jpg) [now](../../20261015-161312/regress.jpg)

### [20261015-163721](../../20261015-163721/status.md) skewed geometry, it should be straight

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-163721`

[report](../../20261015-163721/screenshot.jpg) [NBlood](../../20261015-163721/nblood.jpg) [baseline](../../20261015-163721/baseline.jpg) [now](../../20261015-163721/regress.jpg)

### [20261015-163742](../../20261015-163742/status.md) geomoetry is poking out from the wall

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261015-163742`

[report](../../20261015-163742/screenshot.jpg) [NBlood](../../20261015-163742/nblood.jpg) [baseline](../../20261015-163742/baseline.jpg) [now](../../20261015-163742/regress.jpg)

### [20261015-163841](../../20261015-163841/status.md) this whole in the map is still not fixed, I can see the sky between the ground, for easier replication make sky purple or something while testing.

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261015-163841`

[report](../../20261015-163841/screenshot.jpg) [NBlood](../../20261015-163841/nblood.jpg) [baseline](../../20261015-163841/baseline.jpg) [now](../../20261015-163841/regress.jpg)

### [20261015-215801](../../20261015-215801/status.md) Another grap in geo

e1m1, same 2026-11-15 17:14 (0.02). `gotobug 20261015-215801`

[report](../../20261015-215801/screenshot.jpg) [NBlood](../../20261015-215801/nblood.jpg) [baseline](../../20261015-215801/baseline.jpg) [now](../../20261015-215801/regress.jpg)

### [20261016-084220](../../20261016-084220/status.md) small gap in geo

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261016-084220`

[report](../../20261016-084220/screenshot.jpg) [NBlood](../../20261016-084220/nblood.jpg) [baseline](../../20261016-084220/baseline.jpg) [now](../../20261016-084220/regress.jpg)

### [20261016-084242](../../20261016-084242/status.md) small gap in geo, vertices needs to be merged?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-084242`

[report](../../20261016-084242/screenshot.jpg) [NBlood](../../20261016-084242/nblood.jpg) [baseline](../../20261016-084242/baseline.jpg) [now](../../20261016-084242/regress.jpg)

### [20261016-095818](../../20261016-095818/status.md) small tiney gaps all over the level, build might have truncated the vertices to make them align perfectly?

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-095818`

[report](../../20261016-095818/screenshot.jpg) [NBlood](../../20261016-095818/nblood.jpg) [baseline](../../20261016-095818/baseline.jpg) [now](../../20261016-095818/regress.jpg)

### [20261016-095924](../../20261016-095924/status.md) more ugly tiney gaps

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261016-095924`

[report](../../20261016-095924/screenshot.jpg) [NBlood](../../20261016-095924/nblood.jpg) [baseline](../../20261016-095924/baseline.jpg) [now](../../20261016-095924/regress.jpg)

### [20261019-143458](../../20261019-143458/status.md) small strange geo in the sky here

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261019-143458`

[report](../../20261019-143458/screenshot.jpg) [NBlood](../../20261019-143458/nblood.jpg) [baseline](../../20261019-143458/baseline.jpg) [now](../../20261019-143458/regress.jpg)

### [20261019-162358](../../20261019-162358/status.md) small geo bug is still here

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261019-162358`

[report](../../20261019-162358/screenshot.jpg) [NBlood](../../20261019-162358/nblood.jpg) [baseline](../../20261019-162358/baseline.jpg) [now](../../20261019-162358/regress.jpg)

### [20261019-190259](../../20261019-190259/status.md) looks incorrect

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261019-190259`

[report](../../20261019-190259/screenshot.jpg) [NBlood](../../20261019-190259/nblood.jpg) [baseline](../../20261019-190259/baseline.jpg) [now](../../20261019-190259/regress.jpg)

### [20261020-161118](../../20261020-161118/status.md) slight whole in geo

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261020-161118`, setup `nomonsters | 0.2`

[report](../../20261020-161118/screenshot.jpg) [NBlood](../../20261020-161118/nblood.jpg) [baseline](../../20261020-161118/baseline.jpg) [now](../../20261020-161118/regress.jpg)

### [20261023-090620](../../20261023-090620/status.md) whole in geo after iceblock moves

e2m6, same 2026-11-15 17:14 (0.00). `gotobug 20261023-090620`, setup `send s0 on | 12`. Check: after the floe and strips have moved: no hole between the underwater strip's sloped floor, its side wall and the water ceiling

[report](../../20261023-090620/screenshot.jpg) [NBlood](../../20261023-090620/nblood.jpg) [baseline](../../20261023-090620/baseline.jpg) [now](../../20261023-090620/regress.jpg)

### [20261023-200801](../../20261023-200801/status.md) some incorrect geo at top of closet door

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-200801`. Check: no beams across the ceiling or wooden wall beside the louvered closet door

[report](../../20261023-200801/screenshot.jpg) [NBlood](../../20261023-200801/nblood.jpg) [baseline](../../20261023-200801/baseline.jpg) [now](../../20261023-200801/regress.jpg)

### [20261023-200849](../../20261023-200849/status.md) missing part when opening door

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-200849`, setup `send 224 off | 8`. Check: with the cars 84/88/92 slid to OFF (over the doorway in Build's impossible space) open the door (send 102): its floor stays whole

[report](../../20261023-200849/screenshot.jpg) [NBlood](../../20261023-200849/nblood.jpg) [baseline](../../20261023-200849/baseline.jpg) [now](../../20261023-200849/regress.jpg)

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

### [20261023-233102](../../20261023-233102/status.md) small hole/ missing geo when curtains are open

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-233102`

[report](../../20261023-233102/screenshot.jpg) [NBlood](../../20261023-233102/nblood.jpg) [baseline](../../20261023-233102/baseline.jpg) [now](../../20261023-233102/regress.jpg)

### [20261023-233120](../../20261023-233120/status.md) small hole/ missing geo when door is open

e1m3, same 2026-11-15 17:14 (0.00). `gotobug 20261023-233120`

[report](../../20261023-233120/screenshot.jpg) [baseline](../../20261023-233120/baseline.jpg) [now](../../20261023-233120/regress.jpg)

### [20261024-182730](../../20261024-182730/status.md) slight gap

e2m3, same 2026-11-15 17:14 (0.00). `gotobug 20261024-182730`, setup `warp 2237.1 -2271.5 -32.0 87.7 -27.5 43; nomonsters | 2`. Check: no dotted line across the doorway's ceiling

[report](../../20261024-182730/screenshot.jpg) [NBlood](../../20261024-182730/nblood.jpg) [baseline](../../20261024-182730/baseline.jpg) [now](../../20261024-182730/regress.jpg)

### [20261024-193401](../../20261024-193401/status.md) this room is not correct, collision in the way so can't even enter

e1m5, same 2026-11-15 17:14 (0.02). `gotobug 20261024-193401`, setup `send s210 on | 3`. Check: walk through the double door into the hall: floor, ceiling and pillars as in NBlood, nothing blocking

[report](../../20261024-193401/screenshot.jpg) [NBlood](../../20261024-193401/nblood.jpg) [baseline](../../20261024-193401/baseline.jpg) [now](../../20261024-193401/regress.jpg)

### [20261024-223841](../../20261024-223841/status.md) hole in geo

e1m5, same 2026-11-15 17:14 (0.00). `gotobug 20261024-223841`

[report](../../20261024-223841/screenshot.jpg) [NBlood](../../20261024-223841/nblood.jpg) [baseline](../../20261024-223841/baseline.jpg) [now](../../20261024-223841/regress.jpg)

### [20261025-160556](../../20261025-160556/status.md) small hole in geo

e3m5, same 2026-11-15 17:14 (0.02). `gotobug 20261025-160556`

[report](../../20261025-160556/screenshot.jpg) [NBlood](../../20261025-160556/nblood.jpg) [baseline](../../20261025-160556/baseline.jpg) [now](../../20261025-160556/regress.jpg)

### [20261025-185658](../../20261025-185658/status.md) wall with no collision, lift? compare to NBlood

e3m5, same 2026-11-15 17:14 (0.09). `gotobug 20261025-185658`, setup `send s8 on | 3`. Check: the open north door shows the lit cabin interior (NBlood with sector 8 on); walking in, the cabin's walls block

[report](../../20261025-185658/screenshot.jpg) [NBlood](../../20261025-185658/nblood.jpg) [baseline](../../20261025-185658/baseline.jpg) [now](../../20261025-185658/regress.jpg)

### [20261026-093911](../../20261026-093911/status.md) missing geo when the turning door is in motion

e3m5, same 2026-11-15 17:14 (0.01). `gotobug 20261026-093911`, setup `send 103 on | 3`. Check: mid-turn: the ring's dark floor before the doorway, no lava

[report](../../20261026-093911/screenshot.jpg) [NBlood](../../20261026-093911/nblood.jpg) [baseline](../../20261026-093911/baseline.jpg) [now](../../20261026-093911/regress.jpg)

### [20261026-131012](../../20261026-131012/status.md) geo here looks very wrong

e3m1, same 2026-11-15 17:14 (0.01). `gotobug 20261026-131012`, setup `nomonsters | 0`

[report](../../20261026-131012/screenshot.jpg) [NBlood](../../20261026-131012/nblood.jpg) [baseline](../../20261026-131012/baseline.jpg) [now](../../20261026-131012/regress.jpg)

### [20261026-140737](../../20261026-140737/status.md) texture is upside down

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-140737`, setup `nomonsters | 0`

[report](../../20261026-140737/screenshot.jpg) [NBlood](../../20261026-140737/nblood.jpg) [baseline](../../20261026-140737/baseline.jpg) [now](../../20261026-140737/regress.jpg)

### [20261026-143338](../../20261026-143338/status.md) sky looks strange

e3m2, same 2026-11-15 17:14 (0.00). `gotobug 20261026-143338`, setup `nomonsters | 0`

[report](../../20261026-143338/screenshot.jpg) [NBlood](../../20261026-143338/nblood.jpg) [baseline](../../20261026-143338/baseline.jpg) [now](../../20261026-143338/regress.jpg)

### [20261026-180824](../../20261026-180824/status.md) missing geo

e3m3, same 2026-11-15 17:14 (0.07). `gotobug 20261026-180824`

[report](../../20261026-180824/screenshot.jpg) [NBlood](../../20261026-180824/nblood.jpg) [baseline](../../20261026-180824/baseline.jpg) [now](../../20261026-180824/regress.jpg)

### [20261026-182240](../../20261026-182240/status.md) this looks incorrect

e3m5, same 2026-11-15 17:14 (0.00). `gotobug 20261026-182240`

[report](../../20261026-182240/screenshot.jpg) [NBlood](../../20261026-182240/nblood.jpg) [baseline](../../20261026-182240/baseline.jpg) [now](../../20261026-182240/regress.jpg)

### [20261026-182348](../../20261026-182348/status.md) this looks incorrect

e3m5, same 2026-11-15 17:14 (0.14). `gotobug 20261026-182348`

[report](../../20261026-182348/screenshot.jpg) [NBlood](../../20261026-182348/nblood.jpg) [baseline](../../20261026-182348/baseline.jpg) [now](../../20261026-182348/regress.jpg)

### [20261026-182832](../../20261026-182832/status.md) missing geo

e3m6, same 2026-11-15 17:14 (0.20). `gotobug 20261026-182832`

[report](../../20261026-182832/screenshot.jpg) [NBlood](../../20261026-182832/nblood.jpg) [baseline](../../20261026-182832/baseline.jpg) [now](../../20261026-182832/regress.jpg)

### [20261027-092938](../../20261027-092938/status.md) hole in geo

e1m4, same 2026-11-15 17:14 (0.00). `gotobug 20261027-092938`

[report](../../20261027-092938/screenshot.jpg) [NBlood](../../20261027-092938/nblood.jpg) [baseline](../../20261027-092938/baseline.jpg) [now](../../20261027-092938/regress.jpg)

### [20261027-202540](../../20261027-202540/status.md) hole in geo most likley due to the previous report

e4m2, same 2026-11-15 17:14 (0.00). `gotobug 20261027-202540`, setup `send 116 on | 12`. Check: after the round room 33 and room 201 turn: the stairs whole, no holes

[report](../../20261027-202540/screenshot.jpg) [NBlood](../../20261027-202540/nblood.jpg) [baseline](../../20261027-202540/baseline.jpg) [now](../../20261027-202540/regress.jpg)

### [20261028-174309](../../20261028-174309/status.md) missing wall

mrw01, same 2026-11-15 17:14 (0.01). `gotobug 20261028-174309`

[report](../../20261028-174309/screenshot.jpg) [NBlood](../../20261028-174309/nblood.jpg) [baseline](../../20261028-174309/baseline.jpg) [now](../../20261028-174309/regress.jpg)

### [20261031-113654](../../20261031-113654/status.md) hole in geo

e1m1, same 2026-11-15 17:14 (0.01). `gotobug 20261031-113654`

[report](../../20261031-113654/screenshot.jpg) [NBlood](../../20261031-113654/nblood.jpg) [baseline](../../20261031-113654/baseline.jpg) [now](../../20261031-113654/regress.jpg)

### [20261031-143341](../../20261031-143341/status.md) hole in geo

e1m1, same 2026-11-15 17:14 (0.00). `gotobug 20261031-143341`

[report](../../20261031-143341/screenshot.jpg) [NBlood](../../20261031-143341/nblood.jpg) [baseline](../../20261031-143341/baseline.jpg) [now](../../20261031-143341/regress.jpg)

### [20261031-220911](../../20261031-220911/status.md) train sky sector is making a strange cut in wall/ or renderign is strange here

e1m2, same 2026-11-15 17:14 (0.00). `gotobug 20261031-220911`, setup `cam -2623.57 -1507.77 537.69 135 0 | 2`. Check: the top of the cutting wall runs straight past the train

[report](../../20261031-220911/screenshot.jpg) [NBlood](../../20261031-220911/nblood.jpg) [baseline](../../20261031-220911/baseline.jpg) [now](../../20261031-220911/regress.jpg)

