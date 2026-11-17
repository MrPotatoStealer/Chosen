# 20261020-161118: slight whole in geo

Status: fixed
Regression: stacked-rooms, geometry
Regression setup: nomonsters | 0.2

## History

- 2026-10-20 16:11:18 reported in maps/e1m3.map
- 2026-10-20 fixed. Cause: The strip of the stairwell's (sector 133) east wall in the heights it shares with room 154 was built with a flat top and bottom measured at the wall's middle; the stairwell ceiling slopes along the wall (24 to 56 units), so a triangle above the strip was open onto the orange room beyond.
- 2026-10-20 Fix: Converter: the strips between stacked rooms run between the rooms' real sloped floor and ceiling planes, split where two planes cross. A strip standing on the other room's floor (here room 154's) is drawn without collision, so the taller strip doesn't become an invisible wall there (the reporter of 20261019-190353 had been standing on the old one).
- 2026-10-26 note: Regression setup waits 0.2 s: with Build's collision (2026-10-26) the player slides down sector 133's 29-degree floor (Blood's actFloorBounceVector on landing), as NBlood should; please compare standing on that ramp in NBlood.
