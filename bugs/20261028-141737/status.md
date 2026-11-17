# 20261028-141737: see if we can fix performance here when the boat is triggered

Status: fixed

## History

- 2026-10-28 14:17:37 reported in maps/dwe1m1.map
- 2026-10-31 fixed. Cause: Measured again on the native world at 3440x1440 by replay: the frames round the boat's start (35-40 s) p95 10.2 ms, worst 16.2 ms; the slow frames of the run are in its first 5 s (warm-up). The old live-sector rebuilds are gone. The replay drifts from frame 353 (max 328 units), so the path is only near the report's.
- 2026-10-31 Fix: None needed beyond the native world; reopen if it still feels slow.
