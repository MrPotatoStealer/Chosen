# 20261015-154927: this part of the hedge should look a bit different from the rest to hint that its a secret behind

Status: fixed
Regression: masked-walls

## History

- 2026-10-15 15:49:27 reported in maps/e1m1.bsp
- 2026-10-15 fixed. Cause: Wall palookups were ignored: the plain hedge (wall 861) uses pal 6, the secret (wall 862) pal 0, which is Blood's hint.
- 2026-10-15 Fix: pipeline/pal_textures.py: the converter now writes blood/NNNN_pP (and masked/animated shader) variants for every wall/floor/ceiling palookup a map uses and references them.
