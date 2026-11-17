# 20261101-121241: explosion aftermath looks pretty terrible. Of somthing make it a round distortion around the impact point but very slight distortion

Status: fixed

## History

- 2026-11-01 12:12:41 reported in maps/e2m1.map
- 2026-11-01 fixed. Cause: r_aftermath drew a blocky dust ring on the floor and a noisy shimmer in the Hud pass.
- 2026-11-01 Fix: Ring removed; a slight round ripple goes out from the impact in the Hud pass for 0.45 s, only for explosions in a room seen. Soft translucent sprites kept.
