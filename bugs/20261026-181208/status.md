# 20261026-181208: floating geo

Status: fixed
Regression: stacked-rooms, movers

## History

- 2026-10-26 18:12:08 reported in maps/e3m3.map
- 2026-10-26 fixed. Cause: The renderer guessed the camera's sector (2D tracking, a fresh lookup on jumps) and could pick a room sharing the ground (rooms over rooms around the rotating ring 263)
- 2026-10-26 Fix: The camera's sector is the player's Build sector as Build keeps it (World.SectorTrack), and the shader's walk drops faces of every sector sharing ground with others where the walk reaches another (StackedRooms)
- 2026-10-27 note: 2026-10-27: baseline re-accepted: the old one showed ring 263 drawn part-way from its stored pose (sectors' previous pose unset after trInit, the group never redrawn at rest); the new matches NBlood
