# 20261023-125743: side textures here should be stretching with the coffin lid opens.

Status: fixed

## History

- 2026-10-23 12:57:43 reported in maps/e1m1.map
- 2026-10-23 fixed. Cause: The coffin's side strips (sectors 67/68) keep their area as the lid slides, so they weren't rebuilt: their walls at the moving corners stayed static at a fixed length; and the live walls didn't take the light effect of the sector they belong to (shadeWalls was never written).
- 2026-10-23 Fix: Sectors with a wall at a moving corner are live, those walls drawn whole and stretching (the static world leaves them caulk); fxShadeWalls written and live walls lit by their own sector's effect.
