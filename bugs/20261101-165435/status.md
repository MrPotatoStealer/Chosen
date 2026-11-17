# 20261101-165435: another portal bug with dynamite getting a copy of the sprite stuck in air

Status: fixed
Regression: sprites
Regression check: throw a bundle up through the yard link from the report's spot: one flying bundle, no picture left hanging where it crossed

## History

- 2026-11-01 16:54:35 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: With r_skyfaces 0 a sprite in a sector the walk reaches only through a link has only its copy, and DrawSprite returned there without hiding its own picture (and after placing its shadow), left where it was last placed: a thrown bundle hung where it went through E1M1's yard link, up to 154 units behind the real one.
- 2026-11-01 Fix: SpriteRenderer.DrawSprite: on that return its quad, voxel, flame and shadow are hidden. Replay of the report: 70 frames of the picture away from the bundle before, none after.
