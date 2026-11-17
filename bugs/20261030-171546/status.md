# 20261030-171546: fog should be on ground here but its above in the portal if I use fly

Status: fixed

## History

- 2026-10-30 17:15:46 reported in maps/e1m1.map
- 2026-10-30 fixed. Cause: The converter's mist sectors took the rooftop room over the yard by the gate (sector 90, its floor the sky link down to the yard) as open ground: its mist lay at the link, in the air; the yard itself (65, its ceiling the link) had none
- 2026-10-30 Fix: mist_entities: a sector whose floor is a link is no ground; one under a link up to a room open to the sky counts as open (E1M1 reconverted)
