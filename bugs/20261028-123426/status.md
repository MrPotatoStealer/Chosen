# 20261028-123426: should the car coming at you kill you? check with NBlood

Status: fixed

## History

- 2026-10-28 12:34:26 reported in maps/dwe1m2.map
- 2026-10-28 note: 2026-10-28: the car is DWE1M2's sector 793, a turning sector (616) on channel 101. As in NBlood, the player is pushed out of a turning sector's walls each frame (playerProcess pushmove); pinned against a wall where it can't be, 500 damage: a kill. Waiting on the user: did it kill you, and where were you standing?
- 2026-10-28 note: 2026-10-28 user: the car didn't kill, it pushed the player up on top of it; NBlood's behaviour unknown (to compare). User also sees doors sometimes crush in our port where NBlood pushes the player aside: will report a spot.
- 2026-10-28 note: 2026-10-28 user: parked (not a big deal). No simple fix: NBlood's behaviour isn't known yet, and changing how a turning sector lifts the player touches the step-up and push-out rules other levels rely on.
- 2026-10-31 fixed. Cause: No bug by the code: the car is sector 793, a turning sector, and the game matches NBlood's playerProcess line for line (World.Player.cs:1139-1146, player.cpp:1972-1979): the player is pushed out of its walls, and takes 500 damage only when pinned where pushmove finds no room. So it kills only if it pins you against a wall. Compared in the source, not run side by side in NBlood.
- 2026-10-31 Fix: None; reopen if NBlood behaves differently in play.
