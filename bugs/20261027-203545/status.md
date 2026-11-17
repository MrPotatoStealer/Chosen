# 20261027-203545: small section here where it should be water

Status: fixed
Regression: water
Regression check: swim up from 117 through its ceiling into 118: the view stays underwater all the way

## History

- 2026-10-27 20:35:45 reported in maps/e4m4.map
- 2026-10-28 note: 2026-10-28 user: two water sectors stacked here; the lower one thinks you climb out of the water while you go up into the other water sector. NBlood doesn't do this.
- 2026-10-28 fixed. Cause: Water 117 and water 118 are joined by a stack link (11/12); the underwater view test looked for water at the eye, and the eye over 117's ceiling had none
- 2026-10-28 Fix: World.PlayerProcess: Blood's test, the eye over the ceiling of a sector whose lower link is a water or goo one (levels without Build sectors keep the water-at-the-eye test)
- 2026-10-28 note: 2026-10-28 user: still a surface feel at 117's ceiling. MoveDude set depth and underwater from the medium and found the surface by searching for water above; Blood: depth from Depth or a water/goo link on the sector's floor or ceiling, lift from its own ceiling/floor. 117's ceiling is a stack link: no lift, a smooth rise into 118 (walk test: speed builds steadily through the crossing).
