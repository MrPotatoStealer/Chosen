# 20261016-095536: sprites missing a bit of transparency

Status: fixed
Regression: sprites

## History

- 2026-10-16 09:55:36 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Translucency used the engine default (2/3 opaque for cstat 2, 1/3 with 512); Blood's blend table (screen.cpp bloodglblend) swaps them.
- 2026-10-16 Fix: Sprites, shadows, HUD and translucent masked walls use 1/3 and 2/3 like Blood; cached translucent textures are remade when their level differs.
