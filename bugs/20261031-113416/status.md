# 20261031-113416: can't start boat

Status: fixed

## History

- 2026-10-31 11:34:16 reported in maps/dwe3m1.map
- 2026-10-31 fixed. Cause: No bug: the same as NBlood's code. The switch is wall 1062, the low wall from 148 up to 118. All 11 presses in the recording were standing, the crosshair over the panel: the ray passes above it and lands on 118's floor or the river behind the boat. Standing 12 units away the panel needs a 59 degree look and the look limit is 54.6 (ActionScan and the look formula are NBlood's). Crouched, the next recording's first press hits it and the boat starts.
- 2026-10-31 Fix: None. Crouch, or step back and aim at the panel.
