# 20261024-095834: geo sticking up thru the ground, looks like the closet from down below

Status: fixed
Regression: stacked-rooms, movers

## History

- 2026-10-24 09:58:34 reported in maps/e1m3.map
- 2026-10-24 fixed. Cause: Closet door 122 (a rotating sector inside closet 125) has upper walls up to 125's ceiling, 8 units above the bar 140's floor; whole movers have no cells where the converter finds stacked rooms, so 122/140 was never listed as stacked and the game didn't hide the door from the bar
- 2026-10-24 Fix: Converter: a whole mover is stacked with a room over it when its faces (up to the highest ceiling of it and its neighbours) reach above that room's floor; the mover's materials are then hidden from that room (RegisterMover)
