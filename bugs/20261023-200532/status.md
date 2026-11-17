# 20261023-200532: door hanging down to lower section of train cart

Status: fixed
Regression: stacked-rooms, movers
Regression check: no wall of the upper corridor's sliding door hangs from the train car's ceiling

## History

- 2026-10-23 20:05:32 reported in maps/e1m3.map
- 2026-10-23 fixed. Cause: The upper corridor's sliding door (live sector 159) is stacked over the train car (31) in height; the live renderer's materials weren't marked as stacked, so its moving wall showed inside the car
- 2026-10-23 Fix: LiveRenderer: a stacked live sector's surfaces get their own materials registered with StackedRooms (hidden from the other room as the static faces are)
