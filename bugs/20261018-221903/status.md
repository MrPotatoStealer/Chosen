# 20261018-221903: dynamite is not rotating with sector, also one dynamite fell thru the ground

Status: fixed
Regression: carried-sprites
Regression check: turntable: dynamite rotates with it, none falls through

## History

- 2026-10-18 22:19:03 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: Two bugs. (1) Drag sectors carried nothing: in moverPlace, carry = !(busyProc == 3 || == 4 || == 5) || m.sDrag evaluated to 0 with sDrag 1 (FTEQCC miscompile), so sliding/rotating sectors with Drag (E1M2's turntable and train cars) never carried what stood on them; the riders were detected correctly. (2) A thrown bomb sank through the turntable: the physics moves z before the floor search (as MoveThing), the bomb's origin ended 0.4 below the mover's top, and GetZRange leaves out a mover containing the origin (meant for a door swung into the actor), so the floor became the static floor under the turntable.
- 2026-10-18 Fix: (1) carry spelled out with ifs. (2) GetZRange: when the origin is inside a mover but free of it within the distance the actor moved down this frame (zvel), the mover is its floor and the search starts just above it (zrSunkInto); a door swung into the actor is still left out. Free replay: all three proximity bombs stay on the turntable and rotate with it.
