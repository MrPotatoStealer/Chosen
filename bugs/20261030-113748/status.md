# 20261030-113748: have to spam click to get boat going?

Status: fixed
Regression: movers, collision
Regression setup: nomonsters | 0
Regression check: crouch in the boat facing the lever panel and press use once: the boat sets off

## History

- 2026-10-30 11:37:48 reported in mods/dw/maps/dwe3m1.map
- 2026-10-30 note: Use (ActionScan) traces the brushes: in the boat (DWE3M1 sectors 148-150, 115, 118: sliding, floors below the river's water link at z -480) everything under the river floor is solid, so crouched the trace starts in solid and standing it stops on the river floor before the switch panel (wall 1062, the lower wall of 148 up to 118, txID 103). Only a press landing on sector 118's floor (its pushwall is 1062) worked. NBlood's hitscan follows Build's walls: wall 1062 within 64 starts the boat on the first press.
- 2026-10-30 fixed. Cause: Use traced only the brushes: the boat (sliding sectors 148-150, 115, 118) lies below the river's floor at z -480, solid in the brushes, so a press at the switch panel (wall 1062, the lower wall from 148 up to 118, txID 103) started in solid or stopped on the river floor; only one landing on sector 118's floor (its push wall) worked. NBlood's hitscan follows Build's walls
- 2026-10-30 Fix: ActionScan: where the brush trace finds nothing to use (and no sprite), Build's hitscan on walls and planes now from the player's sector (World.BuildHitScan), stopping at the first wall, floor or ceiling in the way; over 152 recordings it acts only on the boat's switch
