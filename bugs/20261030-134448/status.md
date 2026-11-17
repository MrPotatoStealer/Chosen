# 20261030-134448: make it so when flying using command be able to go through portals

Status: fixed

## History

- 2026-10-30 13:44:48 reported in maps/e3m3.map
- 2026-10-30 fixed. Cause: The flying camera (fly) moved freely, never through links
- 2026-10-30 Fix: FlyCamera: a link plane crossed in its footprint (LinkPathCross) moves it by the link's offset into the room beyond (its layer too)
