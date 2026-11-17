# 20261023-135919: invisible collision infront of me here

Status: fixed

## History

- 2026-10-23 13:59:19 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The live region swept the convex hull of each moving wall run with its fixed neighbours, taking in snow far from the floe; the void there was solid.
- 2026-10-23 Fix: The region takes each run's corners at both ends of its travel and each wall's sweep about its fixed corner.
