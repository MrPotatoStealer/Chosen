# 20261023-145810: still invisible collision here after iceblock

Status: fixed

## History

- 2026-10-23 14:58:10 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: A thin live piece (corners a hair apart) lost a side plane when built as a brush, so the brush ran on unbounded on that side; capped by the risen floe's tilted floor it stood as an invisible wall.
- 2026-10-23 Fix: AddLiveBrush drops near-duplicate corners and any brush reaching past its piece.
