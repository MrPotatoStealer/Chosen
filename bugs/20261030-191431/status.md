# 20261030-191431: hole in geo

Status: fixed

## History

- 2026-10-30 19:14:31 reported in maps/e2m1.map
- 2026-10-30 note: Only with r_waterportals 1 (fine with it off). Not pinned down yet.
- 2026-10-30 fixed. Cause: The converted level's water flats and their seam skirts left a hole beside the lake when drawn as portals.
- 2026-10-30 Fix: Levels now load natively from Blood's .MAP; its water portals are drawn on the sectors' own floors, the dark strip there matches NBlood.
