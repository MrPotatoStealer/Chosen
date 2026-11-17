# 20261023-224444: flowing water ground here seems to push the player the wrong way

Status: fixed

## History

- 2026-10-23 22:44:44 reported in maps/e1m8.map
- 2026-10-24 fixed. Cause: The pan push ignored relative floors: NBlood (actProcessSprites) turns panAngle by the sector's first wall angle + 512 when floorstat 64; E1M8's river sections all have panAngle 0, so we always pushed east
- 2026-10-24 Fix: Converter writes panPushRot for relative pan floors; ActApplyPan adds it to panAngle
