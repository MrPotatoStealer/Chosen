# 20261019-104046: missing sprite when viewed thru portal

Status: fixed
Regression: room-over-room, sprites
Regression setup: send s30 on | 4

## History

- 2026-10-19 10:40:46 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: sprites seen through a portal were oriented from the real camera: a one-sided wall sprite in the linked room (the carved panel) was judged from the other room and hidden
- 2026-10-19 Fix: each sprite takes its rotation frame and one-sided side from the nearer of the real eye and the eye moved by each drawn portal's link offset (the DarkPlaces client's rule); portals are set up before sprites each frame
