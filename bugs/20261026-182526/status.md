# 20261026-182526: falling down when walking here

Status: fixed

## History

- 2026-10-26 18:25:26 reported in maps/e3m5.map
- 2026-10-26 note: 2026-10-26: actors move with Build's clipmove/getzrange on the sectors now (phys_sectors); a walk test across sector 47 at rest doesn't fall. The fall likely needs the lift/crusher 47 in motion: please check in play.
- 2026-10-28 fixed. Cause: Actors moved on the brushes
- 2026-10-28 Fix: Actors move with Build's clipmove/getzrange (phys_sectors), confirmed in play
