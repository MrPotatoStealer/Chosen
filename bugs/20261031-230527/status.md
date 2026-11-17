# 20261031-230527: secondary fire on napalm is not correct, should be like a grenade

Status: fixed

## History

- 2026-10-31 23:05:27 reported in maps/e2m1.map
- 2026-10-31 fixed. Cause: b3d_weaponsv10x was on (the default here): NBlood's v1.0x balance makes the napalm's alt fire shoot like the primary.
- 2026-10-31 Fix: Alt fire is always the thrown ball; the rest of the v1.0x balance stays. Confirmed by replaying the report: thing type 428 appears.
- 2026-11-01 fixed. Cause: b3d_weaponsv10x was on: NBlood's v1.0x balance has the napalm's alt fire shoot like the primary (weapon.cpp 2641-2644), so that is correct with it on.
- 2026-11-01 Fix: No change in the end: an override that always threw the ball was taken out at the reporter's word; with the balance off the alt fire throws the ball.
