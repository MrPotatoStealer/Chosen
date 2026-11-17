# 20261020-101619: lets add an option to turn off the auto aim on player weapon,

Status: fixed

## History

- 2026-10-20 10:16:19 reported in maps/e1m1.map
- 2026-10-20 fixed. Cause: The game logic had NBlood's auto aim modes (gAutoAim) but always ran with 1 (always); no setting or menu entry.
- 2026-10-20 Fix: cl_autoaim (saved, NBlood's: 0 never, 1 always, 2 hitscan only, 3 pitchfork only), Options > Game > Auto aim. Applied at level start and at once when changed; recordings store it (and a mid-level change as setautoaim), so replays match. Old recordings play with 1.
