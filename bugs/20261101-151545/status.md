# 20261101-151545: still stuttering framerate here, look into it

Status: fixed

## History

- 2026-11-01 15:15:45 reported in maps/cp01.map
- 2026-11-01 fixed. Cause: Each step of an animated picture gave its material a new texture, which made Godot rebuild all that uses the material; with water portals on that is many surfaces, 7.5 times a second for water: frames of 33 ms in a 5 ms game (earlier wrongly closed as caused by test runs).
- 2026-11-01 Fix: NativeWorld: an animated picture has one texture whose pixels are updated (AnimatedPicture), the material untouched. On the CP01 dock in real time at 3440x1440: frames over 20 ms from about 108 to 2 and 0, p99 from 35 ms to 6.4 and 7.6.
