# 20261027-093224: dont rotate with carousle

Status: fixed
Regression: collision, movers
Regression check: walk from room 352 onto the carousel (e1m4-carousel): standing still, it carries you round, feet at -128

## History

- 2026-10-27 09:32:24 reported in maps/e1m4.map
- 2026-10-27 fixed. Cause: The converter gives room 352 the area its whole movers leave (carousel 321 and its ring), so sector tracking kept the player in 352 (outline still holds them) on the carousel: walking at 352's floor -160 inside the carousel, never carried
- 2026-10-27 Fix: UpdateSector prefers a whole moving sector holding the point and the sprite's span over the current sector's outline (Build's room has no area where the mover is)
