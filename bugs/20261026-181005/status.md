# 20261026-181005: does sound travel correctly through portals?

Status: fixed

## History

- 2026-10-26 18:10:05 reported in maps/e3m3.map
- 2026-10-26 note: User: sound seems cut off when going through a portal like water.
- 2026-10-26 fixed. Cause: Sounds were heard from the camera, which views from the room beyond a water link (1200 units away in Build's map) while the eye is past the plane; and they faded with DarkPlaces' steep curve (almost silent at 800 units)
- 2026-10-26 Fix: Heard from the player's position (Blood's gMe sprite) through an AudioListener3D turned as the view; fade as Blood's Calc3DValues (64 / max(3/8 d, 64))
