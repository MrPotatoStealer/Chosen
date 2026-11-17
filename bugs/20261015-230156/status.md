# 20261015-230156: piano secret not working,

Status: fixed

## History

- 2026-10-15 23:01:56 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the piano keys are invisible pushable x-sprites; Blood's action scan hits sprites with cstat 4096 (trInit sets it for Push sprites) even when invisible and non-blocking, ours only hit solid sprites
- 2026-10-15 Fix: ActionScan switches nearby sprites to Blood's rule for the scan: cstat 4096 sprites become hittable, other sprites are ignored
