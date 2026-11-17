# 20261027-103605: using noclip and flying to the chest location and it looks all messed up

Status: fixed

## History

- 2026-10-27 10:36:05 reported in maps/e1m3.map
- 2026-10-27 fixed. Cause: The sectors' previous pose (PrevOrigin/PrevYaw) wasn't set after trInit turned and slid the movers to OFF: a frame drawn before the first game frame drew the chest's group between the stored pose (or the map origin) and OFF, and once still the group was never drawn again (stuck, no flicker; the keyhole sprite in place)
- 2026-10-27 Fix: After TrInit every sector's and sprite's previous pose is its pose; LiveRenderer draws a group again at its pose once it stops if it was last drawn between game frames
