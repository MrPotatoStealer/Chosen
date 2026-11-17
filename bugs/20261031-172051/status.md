# 20261031-172051: flame is very dull, why not bright like a napalm explosion?

Status: fixed

## History

- 2026-10-31 17:20:51 reported in maps/mrw01.map
- 2026-10-31 fixed. Cause: MARROW's fires are decorations of flame tile 2101 at their sector's shade, not fullbright (NBlood draws them that way too), so HDR's highlights, for fullbright sprites only, passed them by.
- 2026-10-31 Fix: With HDR's highlights on, tile 2101's frames draw fullbright; SDR unchanged. Weapons' fullbright animation layers take highlights too.
