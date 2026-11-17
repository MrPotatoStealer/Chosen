# 20261102-085848: look into why this big black thing is in the sky

Status: fixed
Regression: stacked-rooms
Regression setup: cam -82.9 -754.3 -452.9 358.8 -22.5 | 1
Regression check: no black panel with a white tree over the hedge: sky and trees as in NBlood

## History

- 2026-11-02 08:58:48 reported in maps/dwe3m1.map
- 2026-11-02 fixed. Cause: Black backstage rooms (941 and 836, shade 63, palette 0, no sky) overlap the lake's open sectors in plan and in height; the visibility walk passed a sector's window on to every sector overlapping it as if seen directly, so 941 took its sky neighbour's window open upward and the walk went on into 836, whose wall stood black above the hedge (r_skyfaces 0 draws only what the walk reaches). Build reaches a sector only through its walls or a link.
- 2026-11-02 Fix: Sectors that merely overlap pass their window on as seen (moving sectors still rebuilt), not as seen directly, so their faces aren't drawn; rooms stacked through Stacks keep the direct window. Regress all, motion unchanged; DWE3M1 closer to NBlood (0.484 to 0.440), E1M1 unchanged, E3M1's drift there before this change.
