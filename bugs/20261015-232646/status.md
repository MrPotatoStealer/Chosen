# 20261015-232646: missing firsre texture hiding the inner part (secret) of the oven

Status: fixed
Regression: masked-walls

## History

- 2026-10-15 23:26:46 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: the oven mouth is a Build one-way wall (cstat 32): overpicnum drawn from one side only and passable; not converted
- 2026-10-15 Fix: one-way walls become a non-solid slab with a single-sided blood_oneway texture, pegged like polymost (own ceiling with cstat 4, else next ceiling)
