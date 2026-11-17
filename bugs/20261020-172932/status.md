# 20261020-172932: missing collision

Status: fixed

## History

- 2026-10-20 17:29:32 reported in maps/e1m4.map
- 2026-10-20 note: Recorded while the game logic was stuck (the E1M4 tick exception of 20261020-173031, from frame 192). The 'cage' is a spinning turnstile: sector 151 rotates with four blocking wall sprites (pic 332) laid out as a + around its centre; their collision boxes exist, are solid and turn with them (hitscans from the report spot hit them as they turn). Needs a retest with the tick fix.
- 2026-10-22 fixed. Cause: Retested by the user: the turnstile collision works (fixed by the earlier E1M4 collision work)
- 2026-10-22 Fix: No further change
