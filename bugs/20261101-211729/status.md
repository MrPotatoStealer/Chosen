# 20261101-211729: can we make the dynamic light from fire/torches flicker a bit

Status: fixed

## History

- 2026-11-01 21:17:29 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: Flames' lights changed by at most 15% with a steady place: hardly seen.
- 2026-11-01 Fix: Stronger flicker about the same mean, and the light moves a little with it, shadows too; r_lightflicker (Enhanced > Lighting). Frame time the same at 3440x1440, about 6 ms.
