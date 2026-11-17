# 20261018-221630: UI is not aligned correctly, red, blue, green bars etc and keys

Status: fixed

## History

- 2026-10-18 22:16:30 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: TileHGauge (armor bars, TNT throw power) drew its tile centred on (x, y) with the tile offsets; NBlood's TileHGauge draws with rotatesprite flag 16 (stat | 90), anchored at the top left corner, so every gauge sat half a tile up and to the left of its slot (in both HUD sizes).
- 2026-10-18 Fix: TileHGauge anchors the tile's top left corner at (x, y) (qc/client/hud.qc). The key icons were already at NBlood's positions (gViewSize 2: x = 73 + (i>>1)*10 on the left, 320 - (78 + (i>>1)*10) right-aligned, y 194).
