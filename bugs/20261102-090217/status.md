# 20261102-090217: lots of geo issues on this level

Status: fixed
Regression: stacked-rooms

## History

- 2026-11-02 09:02:17 reported in maps/dwe4m1.map
- 2026-11-02 fixed. Cause: Rooms that only overlap others in plan (room-in-room) passed the window on as seen directly, so their walls were drawn wherever the visibility walk reached a sector they overlap (DWE4M1: walls of other rooms all over; same cause as 20261102-085848).
- 2026-11-02 Fix: Overlapping sectors no longer count as seen directly (SectorVisibility._partnerDirect); confirmed in the game. NBlood's shot here is black (its camera lands in another overlapping sector: -b3dview takes no sector).
