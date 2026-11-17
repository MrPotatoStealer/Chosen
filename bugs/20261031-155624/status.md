# 20261031-155624: moving while on the boat sort of bounces you back, check against NBlood

Status: fixed

## History

- 2026-10-31 15:56:24 reported in maps/dwe3m1.map
- 2026-10-31 fixed. Cause: As NBlood's source: on the boat only what stands on the floor of a Drag sector is carried (TranslateSector: floorZ <= bottom, the sprite's picture's bottom). Starting to walk changes the player's sequence in playerProcess, before trProcessBusy, and the walking picture's bottom is a little higher: that one game frame the boat doesn't carry them (frames 573, 588, 717 of the recording: 2 units against the boat's 9). In the air, stepping down off a bench, the same for 3 frames (618-620, 699-701). The order and the test are the same in player.cpp, blood.cpp and triggers.cpp. Compared in the source, not played side by side.
- 2026-10-31 Fix: None: it matches NBlood.
