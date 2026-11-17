# 20261030-191531: water in boat, should not be there

Status: fixed

## History

- 2026-10-30 19:15:31 reported in maps/e2m1.map
- 2026-10-30 note: Also without transparent water: the lake 125's live sweep fills the boat's inner sectors 91-93 (whole movers on channel 115 that don't border 125, so they aren't in LiveCluster.Rigid; only 87's outline is cut out).
- 2026-10-30 fixed. Cause: The converted ice block's live tiles kept the water flat inside it.
- 2026-10-30 Fix: Native level: the block is Build's sector, no water drawn in it.
