# 20261023-171814: looks like there is a ceiling following along with the train causing a strange issue

Status: fixed

## History

- 2026-10-23 17:18:14 reported in maps/e1m2.map
- 2026-10-23 fixed. Cause: The level was the 12:36 conversion (before the converter's live-region changes): a brick wall strip rose above the track wall behind the train. Replaying the recording on the reconverted level shows the view without it; the dark patch in the sky is the sky picture (NBlood's sky offset differs). Also: the live file kept blood_sky/ wall pictures (the .map's are renamed sky/), drawn flat.
- 2026-10-23 Fix: Reconverted E1M2; prepare_map renames blood_sky/ in the .live file too.
