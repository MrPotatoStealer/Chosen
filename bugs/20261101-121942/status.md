# 20261101-121942: glowing pixels causing some strange issues on some sprites

Status: fixed

## History

- 2026-11-01 12:19:42 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: r_glow picked pixels by colour alone, so any bright speck on any picture glowed at full brightness in the dark.
- 2026-11-01 Fix: Glow only on what gives light: lit torches, light fixture and flame pictures, fullbright sprites, and in the level lava, fire and fixtures (BuildShading.GlowTile); colours within those as before.
