# 20261026-154529: still geo here that should not be there

Status: fixed

## History

- 2026-10-26 15:45:29 reported in maps/e3m2.map
- 2026-10-26 note: 2026-10-26: the camera's sector is now the player's tracked Build sector, switching to a sector sharing the ground when the player leaves its column (a noclip flight down into 283): please check by flying in again.
- 2026-10-28 fixed. Cause: The camera's sector was looked up from the position, not the player's tracked Build sector
- 2026-10-28 Fix: The camera uses the player's tracked sector (confirmed in play)
