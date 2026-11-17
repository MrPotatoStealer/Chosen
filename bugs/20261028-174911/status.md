# 20261028-174911: trees are cutoff, think we need to change how rendering of sky is done. Render Sky first and just don't render anythign at all when a brush has a sky texture on them

Status: fixed

## History

- 2026-10-28 17:49:11 reported in maps/cp09.map
- 2026-10-28 fixed. Cause: Sky faces wrote their own depth: a tree sprite rising past its room's sky ceiling was cut at the ceiling's height (Build draws the sky behind everything its walk reaches)
- 2026-10-28 Fix: BuildSky: a sky face's depth lies r_skydepth (256) units further along the view; it is also shaded by its sector's ceiling shade (MARROW and Death Wish skies were too bright)
