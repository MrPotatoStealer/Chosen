# 20261025-160556: small hole in geo

Status: fixed
Regression: geometry

## History

- 2026-10-25 16:05:56 reported in maps/e3m5.map
- 2026-10-25 fixed. Cause: The 4-unit upper wall over E3M5's scrolling waterfall wall 42 (sector 4 to 123) belongs to wall 942, which does not scroll, but the converter flagged the whole wall pair as scrolling (wpan), so the sectors renderer skipped it and the scrolling skin covered only the lower part: lava showed through the strip.
- 2026-10-25 Fix: The .sectors flag is per part: wpan for a one-sided wall, wpan_lower / wpan_upper when the wall Build draws that part with scrolls; LiveRenderer.Sectors skips only those parts.
