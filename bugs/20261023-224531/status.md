# 20261023-224531: diving suit ui visor is missing under water

Status: fixed

## History

- 2026-10-23 22:45:31 reported in maps/e1m8.map
- 2026-10-23 fixed. Cause: The HUD never drew the diving suit's visor (NBlood viewDrawScreen: tile 2344 in the four corners and the glints 2346/2347 while the suit is active), nor the asbestos armor's hood (2358)
- 2026-10-23 Fix: Hud.DrawGear draws both over the view after the weapon, as NBlood; the cousteau cheat now gives 200% health and the diving suit as in NBlood (it was noclip)
