# 20261021-083512: voodoo doll secondary fire does nothing?

Status: fixed

## History

- 2026-10-21 08:35:12 reported in maps/e1m4.map
- 2026-10-21 note: Replay (exact, max error 0): alt fire at frame 2717 with enemies in view; by 2760 (no fire pressed) six dudes lost 160-685 health and two died, the doll was used up (AltFireVoodoo: all ammo, weapon removed), as NBlood. Damage scales with remaining ammo and distance (51200) and the dude's spirit factor at skill 4. Asked the user what they expected.
- 2026-10-22 fixed. Cause: Not a bug: the voodoo doll's alt fire works (seen in the replay; user confirmed)
- 2026-10-22 Fix: No change
