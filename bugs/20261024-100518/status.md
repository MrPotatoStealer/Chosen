# 20261024-100518: I'm pretty sure the zombies here should come alive, lets check with NBlood

Status: fixed
Regression: collision, masked-walls
Regression check: the axe zombies behind the broken wall wake and come through the hole

## History

- 2026-10-24 10:05:18 reported in maps/e1m4.map
- 2026-10-24 fixed. Cause: The broken-wall hole in front of the axe zombies is a masked wall with cstat 64 (blocks hitscans only). The converter made every blocking masked wall solid caulk, so the AI's cansee (and the player) were stopped by it; Build's cansee passes masked walls and clipmove only tests cstat 1.
- 2026-10-24 Fix: Masked walls' hidden faces take tools/maskmove (cstat 1: Contents.WallMove, in MaskMove/MaskBlock), tools/maskshot (cstat 64: WallShot, in MaskShot) or tools/maskwall; QCanSee's Solid|BotClip passes them. The zombies wake and come through the hole.
