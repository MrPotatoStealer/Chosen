# 20261018-152413: switch pressed but train is not moving, supposed to rotate

Status: fixed
Regression: movers
Regression check: press the switch: the turntable rotates with its sprites

## History

- 2026-10-18 15:24:13 reported in maps/E1M2.bsp
- 2026-10-18 fixed. Cause: The train turntable is a step-rotating sector (type 613, kSectorRotateStep), which the converter did not handle: sectors 3, 4, 11 and 311 stayed static world. The server's StepRotateBusy only set the angle (no riders, carried sprites or relink).
- 2026-10-18 Fix: Converter builds 613 sectors as whole movers at their stored angle with rotOn = marker angle and an origin brush at the marker; the car (bordering only movers) takes the room of the group, and the turntable's whole outline is open space of that room. StepRotateBusy places the mover through moverPlace with the step's start angle (stepOfs), so the Drag flag carries the player. Verified: each switch press turns the train 45 degrees. Also fixed in this round: E1M2 movement jitter (client linkCrossOffset took ordinary movement for a crossing of the 4 -4 0 link).
