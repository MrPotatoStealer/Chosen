# 20261016-095924: more ugly tiney gaps

Status: fixed
Regression: geometry

## History

- 2026-10-16 09:59:24 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Textured end faces added to wall skins at 'outside corners' also appeared on nearly straight wall joints, as pale 0.05-wide lines without a Build surface.
- 2026-10-16 Fix: End faces removed; skins on the surfaces close the corners exactly.
