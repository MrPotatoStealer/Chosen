# 20261016-110712: burning tree animations make it move sideways during some of it frames. Like center is shifted

Status: fixed
Regression: sprites

## History

- 2026-10-16 11:07:12 reported in maps/E1M1.bsp
- 2026-10-16 fixed. Cause: Face sprites moved by their x offset in the wrong direction (right); Polymost moves them left (s0.x -= off.x) and odd-width tiles half a texel right, so animation frames with different offsets jumped sideways.
- 2026-10-16 Fix: Face sprite offset negated like Polymost, plus the half-texel shift for odd widths.
