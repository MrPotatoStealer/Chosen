# 20261101-122328: make mist on ground more consistent, vieweing from here it's all gone until I jump down on the ground

Status: fixed

## History

- 2026-11-01 12:23:28 reported in maps/e1m1.map
- 2026-11-01 fixed. Cause: Mist was switched off for everything behind an active portal; from the rooftops the yard is seen only through portal 2, so its mist vanished until the player was down in it.
- 2026-11-01 Fix: GroundMist: behind a portal the fog shader samples the far room mist by the link offset, areas in view through a portal count as in view. Mist seen in the replayed report from the same roof.
