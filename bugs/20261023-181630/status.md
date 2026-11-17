# 20261023-181630: still some strange sky geo following along with the train you can see it on the wall behind changing height, it should look to be all the same height

Status: fixed

## History

- 2026-10-23 18:16:30 reported in maps/e1m2.map
- 2026-10-23 fixed. Cause: The flat car's sky ceiling (568) sits under the track's (592); its solid's sky faces hid the tunnel's back wall between the two heights, a band moving with the train. Build draws no wall between two skies: the rooms beyond show up to their own.
- 2026-10-23 Fix: A whole mover's sky ceiling solid starts at the highest sky ceiling among its neighbours.
