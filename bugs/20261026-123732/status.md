# 20261026-123732: sound when standing in lava is repeated to fast, sound wrong

Status: fixed

## History

- 2026-10-26 12:37:32 reported in maps/e3m5.map
- 2026-10-26 fixed. Cause: The sound system ignored sfxPlay3DSound's flags: standing in lava plays the sizzle (352, channel 5, flag 2) and the burn pain sound (channel 0, flags 6) every frame, and each restarted the last one, where NBlood keeps the one still playing
- 2026-10-26 Fix: World.SoundPlayed carries the flags; SoundSystem.OnSound drops the new sound while the channel plays the same one (2) or any (4), anyone's with 1
