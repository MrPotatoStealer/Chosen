# 20261021-083436: using voodoo doll without enemies nearby should damage player instead

Status: fixed

## History

- 2026-10-21 08:34:36 reported in maps/e1m4.map
- 2026-10-21 note: The recording ran testkit (god mode) at frame 9: NBlood's playerDamageSprite returns 0 for every damage type under god mode (damageControl), so the doll's miss (QAV 107 trigger 5: actDamageSprite(self, bullet, 1<<4), 1 HP) does nothing there too. Port code and QAV data match NBlood. Asked the user to retry without god mode.
- 2026-10-22 fixed. Cause: Not a bug: the reporter had god mode on, which blocks the voodoo doll's self-damage
- 2026-10-22 Fix: No change (user confirmed)
