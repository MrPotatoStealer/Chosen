# 20261026-120645: platform should be rotating and moving at the same time

Status: fixed
Regression: movers
Regression setup: send 7 on | 1.5
Regression check: the pillar is half-turned as it slides (NBlood: it turns 180 degrees across the room)

## History

- 2026-10-26 12:06:45 reported in maps/e3m5.map
- 2026-10-26 fixed. Cause: The converter ignored a sliding sector's marker angles: NBlood's TranslateSector also turns the sector from the first marker's angle to the second's as it slides (E3M5's lava-room pillar, 1024 to 0: half a turn)
- 2026-10-26 Fix: convert_map.plan_doors keeps the turn when it lands on the stored shape at ON (pivot at the second marker, which travels from the first; origin brush at the OFF pivot); E2M3's two mixed-sign panels keep the warning. Also turns slides in E1M8, E2M1, E2M9, E3M4
