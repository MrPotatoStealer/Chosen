# 20261031-220610: fix leafes in wind so they don't spin like crazy, increase the amount.

Status: fixed

## History

- 2026-10-31 22:06:10 reported in maps/e1m1.map
- 2026-10-31 fixed. Cause: The leaves' spin came from their position, which the wind moves, so the harder it blew the faster they spun (snow flakes' size flickered the same way).
- 2026-10-31 Fix: Spin and size from the particle's own constants, slower with a flutter; four times as many leaves.
