# 20261102-090029: napalm going through enemies

Status: fixed
Regression: collision

## History

- 2026-11-02 09:00:29 reported in maps/dwe3m1.map
- 2026-11-02 fixed. Cause: Blood's own: a missile isn't solid (napalm's SEQ 61 frames not blockable, so the sequence clears its cstat 1 as NBlood's does) and Build's clip lines stop only what crosses them from outside. A gill beast walking at the napalm stepped onto it between two game frames, its own move never meeting the missile, and the missile, starting inside the beast's box, flew on through (both shots in the recording).
- 2026-11-02 Fix: FIX BLOOD'S BUGS (b3d_fixblood): a missile starting its move inside a dude's box hits that dude (ClipMoveB, MoveMissileSectors). Off: as Blood. Replaying the recording with it on: the first shot hits at frame 2405.
