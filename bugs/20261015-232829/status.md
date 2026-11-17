# 20261015-232829: missing texture hiding the secret (cove) in the wall

Status: fixed
Regression: masked-walls

## History

- 2026-10-15 23:28:29 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the fake wall hiding the cove is a one-way wall (cstat 32) seen from sector 111
- 2026-10-15 Fix: one-way wall support (see 20261015-232646)
