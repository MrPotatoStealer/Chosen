# 20261019-131556: text is not aligned correctly at the top saying Episodes

Status: fixed

## History

- 2026-10-19 13:15:56 reported in maps/e1m2.map
- 2026-10-19 fixed. Cause: the menu titles used font 2 centred by its capitals' height; NBlood (CGameMenuItemTitle) uses font 1 at shade -128 centred by the font's tallest glyph, and items have their top at the given y
- 2026-10-19 Fix: menu layout follows NBlood menu.cpp (titles font 1 white, items from y 45/50, episodes from SetupEpisodeMenu's offset), the title background at shade 40, and the dripping blood QAV (BDRIP, itemBloodQAV) tiled across wide screens
