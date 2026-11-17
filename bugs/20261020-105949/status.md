# 20261020-105949: how do I swim up and down?

Status: fixed
Regression: water
Regression check: play <id>: at the end 'where' shows posture 1 medium 1 underwater True; the screen is in the water palette; jump/crouch swim up/down

## History

- 2026-10-20 10:59:49 reported in maps/e1m4.map
- 2026-10-20 fixed. Cause: E1M4's pools are Blood underwater sectors joined to the surface by water links, with no liquid brushes. Crossing the link set the player swimming, but the port's water test only looked at liquid brushes, so the next check took the player back out of the water: standing posture, normal medium, so jump and crouch didn't swim and IsUnderwater stayed false (no underwater view). The underwater view itself was also a guessed tint, not Blood's palette.
- 2026-10-20 Fix: Sim: a point is in water inside a liquid brush or a sector Blood marks underwater (World.InUnderwaterSector, used by CheckLink and PointIsWater). Render: Blood's screen palettes (WATER, BEAST, SEWER, INVULN1; build_assets now exports them to gfx/*.lmp) as a colour matrix fitted to BLOOD.PAL, drawn over the whole screen (ViewPalette, Hud), chosen as viewDrawScreen does (death mask, reflective shots, underwater by medium).
