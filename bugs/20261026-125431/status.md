# 20261026-125431: sound when firing shotgun is cutoff

Status: fixed

## History

- 2026-10-26 12:54:31 reported in maps/e1m2.map
- 2026-10-26 fixed. Cause: The sound system folded NBlood's channels into 1..7 (DarkPlaces' sndMapChannel): a landing blood drip's sound (385, channel 30, flag 1: on anyone's channel) fell on the same channel as the player's shot (2), so each drip a shotgun blast threw stopped the shot's sound
- 2026-10-26 Fix: SoundSystem keeps NBlood's channel numbers as they are (-1: none)
