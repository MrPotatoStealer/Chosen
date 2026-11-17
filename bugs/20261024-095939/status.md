# 20261024-095939: player not being push along the correct flow of the river

Status: fixed

## History

- 2026-10-24 09:59:39 reported in maps/e1m8.map
- 2026-10-24 fixed. Cause: The pan push ignored relative floors: NBlood (actProcessSprites) turns panAngle by the sector's first wall angle + 512 when floorstat 64; E1M8's river sections all have panAngle 0, so we always pushed east
- 2026-10-24 Fix: Converter writes panPushRot for relative pan floors; ActApplyPan adds it to panAngle
