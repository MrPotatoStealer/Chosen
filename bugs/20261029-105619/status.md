# 20261029-105619: got crushed trying to ride the boat

Status: fixed

## History

- 2026-10-29 10:56:19 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: the boat's five sectors move as separate movers: the hull pushed the player standing on the deck sector before that one carried him, and he was crushed
- 2026-10-29 Fix: movers in lockstep (World.LockstepOf) carry each other's riders with the first that moves
