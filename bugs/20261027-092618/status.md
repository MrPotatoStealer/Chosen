# 20261027-092618: chest here in the wrong position

Status: fixed

## History

- 2026-10-27 09:26:18 reported in maps/e1m3.map
- 2026-10-27 note: Not reproduced: the replay never opens the chest (the use presses at 2624-2678 miss it, as the session's sim must have) and at frame 2829 it's drawn closed with its keyhole, as NBlood. The report shows it turned to the stored (open) shape with the keyhole left in place: a drawing-only glitch. Asked the user whether it flickers or stays, and how it got there
- 2026-10-27 fixed. Cause: The sectors' previous pose (PrevOrigin/PrevYaw) wasn't set after trInit turned and slid the movers to OFF: a frame drawn before the first game frame drew the chest's group between the stored pose (or the map origin) and OFF, and once still the group was never drawn again (stuck, no flicker; the keyhole sprite in place)
- 2026-10-27 Fix: After TrInit every sector's and sprite's previous pose is its pose; LiveRenderer draws a group again at its pose once it stops if it was last drawn between game frames
