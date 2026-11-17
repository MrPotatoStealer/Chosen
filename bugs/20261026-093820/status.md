# 20261026-093820: some z-figting on top of the platform, moving the textures don't follow (global mode)

Status: fixed
Regression: stacked-rooms, movers
Regression setup: nomonsters | 0
Regression check: pillar tops one texture, no z-fighting; moving, the texture moves with the pillar

## History

- 2026-10-26 09:38:20 reported in maps/e3m5.map
- 2026-10-26 fixed. Cause: The pillar top was drawn twice in one plane: the upper pillar's floor (sector 212, aligned to its first wall, so it follows the platform) and, through the lava room's floor portal (link 30), the lower pillar's floor (213, a zero-height sector at the link plane, world-aligned: the texture that did not follow). The portal copy keeps faces on the plane, so the two fought.
- 2026-10-26 Fix: Portal copies are drawn 4 parts in 10000 further away (world shaders' vertex stage, on top of a mover's push): the near room's own face wins a tie at the plane, as Build draws the far room only through the portal floor's pixels.
