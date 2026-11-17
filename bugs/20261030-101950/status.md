# 20261030-101950: crouching in boat made me push into water

Status: fixed
Regression: water, collision
Regression setup: nomonsters | 0
Regression check: stand in the boat and crouch: you stay in it, not under the river

## History

- 2026-10-30 10:19:50 reported in maps/dwe3m1.map
- 2026-10-30 fixed. Cause: A link plane was crossed wherever its footprint held the sprite: DWE3M1's boat (sliding sectors 148-150 in the river's outline, their floors below its water link) - crouching lowers the player's middle below the plane, so the river's link took them under it. Blood only follows the links of the sprite's own sector
- 2026-10-30 Fix: CheckLinkCross skips a link whose sector is neither the sprite's Build sector nor the link's far one (also: E3M2's gibs on the floor of the room under a linked one no longer go through)
