# 20261019-113150: unable to destory lights, should make the sector darker

Status: fixed
Regression: sector-lighting
Regression check: break all three ceiling lights: the corridor goes dark (two are not enough)

## History

- 2026-10-19 11:31:50 reported in maps/e1m1.map
- 2026-10-19 note: Shooting a light hits and operates the switch, but Godot never showed a switch's on frame (the DarkPlaces server's spriteAnimSel): fixed, broken lights now show broken. Darkening: the lights count into two switch_combo counters (data2 3) that send channel 124 to light sectors 67/72-74, which start off (busy 0) and brighten when on; after three broken lights the combos fire but the sectors stay off. Needs a check against NBlood of what Blood does here.
- 2026-10-19 fixed. Cause: works as intended (confirmed in NBlood): the corridor lights feed counters that switch channel 124 off only when all three ceiling lights are broken; broken lights now also show their broken tile
- 2026-10-19 Fix: switch on/off tiles (spriteAnimSel) drawn; nothing else needed
