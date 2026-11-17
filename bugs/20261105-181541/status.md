# 20261105-181541: the improved water rendering needs to support water under water links, right now it only shows the water in the sector you currently are

Status: fixed

## History

- 2026-11-05 18:15:41 reported in maps/test.map
- 2026-11-05 fixed. Cause: the water fog (r_waterfog, r_caustics) counted the eye's line of sight in water only in the level itself and in copies through a water surface; the room beyond a link joining two rooms under water (Forge's water under water, E4M4's links 13 and 25) is a portal copy of a plain stack link, so it had none
- 2026-11-05 Fix: a portal joining two underwater sectors writes w 2 in its portal_data row (PortalRenderer Submerged, from the sectors' Underwater flags) and water_path counts all of the line of sight in its copy with the eye under water
