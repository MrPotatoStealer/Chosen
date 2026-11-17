# 20261019-122949: missing transparency on windows

Status: fixed
Regression: masked-walls

## History

- 2026-10-19 12:29:49 reported in maps/e1m1.map
- 2026-10-19 fixed. Cause: translucent masked walls (cstat 128, opacity baked into blood_masked/NNNN_tT) were drawn opaque: the world shader cut alpha at 0.5
- 2026-10-19 Fix: blood_masked/*_tN surfaces use a blended, two-sided copy of the world shader
