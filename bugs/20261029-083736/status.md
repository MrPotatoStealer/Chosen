# 20261029-083736: incorrect slicing when boat is moving?

Status: fixed

## History

- 2026-10-29 08:37:36 reported in maps/dwe1m1.map
- 2026-10-29 fixed. Cause: the boat's sloped decks kept their slopes where they were built: thousands of units away the walls between them reached to the map's top and bottom
- 2026-10-29 Fix: a live sector carried whole by a mover takes its slopes along (World.SlopeZ); tile walls look up what they end at in the tile's own pieces
