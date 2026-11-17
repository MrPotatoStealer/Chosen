# 20261019-122718: sector light don't go out when destroying lights

Status: fixed

## History

- 2026-10-19 12:27:18 reported in maps/e1m1.map
- 2026-10-19 note: In the recording the corridor lights were switched on and two of the three ceiling lights shot. The lights feed counters (switch_combo data2 3) that turn channel 124 off only after all three are broken; breaking all three darkens the corridor in Godot. Same logic as 20261019-113150: to confirm against NBlood.
- 2026-10-19 fixed. Cause: works as intended (confirmed in NBlood): only two of the three lights were broken; the corridor goes dark when all three are
- 2026-10-19 Fix: none needed
