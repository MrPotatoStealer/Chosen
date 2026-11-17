# 20261021-083322: should be able to shoot through this window, but prevent player collision

Status: fixed
Regression: collision
Regression check: shoot the window grate: bullets go through, the player can't walk through

## History

- 2026-10-21 08:33:22 reported in maps/e1m4.map
- 2026-10-21 fixed. Cause: The window grate is a sprite with cstat 1 (blocking) without 256 (hitscan): Build's hitscans (CLIPMASK1) pass it, movement (CLIPMASK0) doesn't. The port gave every blocking sprite Body contents, which MaskShot includes.
- 2026-10-21 Fix: Contents.Blocker (128): sprites blocking movement only (in MaskMove/MaskZRange, not MaskShot); HitScan takes a mask, CLIPMASK0 callers (missile spawn, actFireThing, CanMove) pass MaskBlock; action scan keeps cstat-4096 Blocker sprites.
