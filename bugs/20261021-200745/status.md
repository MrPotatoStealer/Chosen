# 20261021-200745: differen swimming speeds here and next to it, I make anothe report

Status: fixed

## History

- 2026-10-21 20:07:45 reported in maps/e2m2.map
- 2026-10-21 fixed. Cause: E2M2's pool (Build sectors 152/153) is an underwater z-motion sector: its blood_sector entity is only the moving ceiling, and the sector lookup (SectorAtPoint) left movers out, so in the pool a dude had no underwater sector and MoveDude applied ground drag near the floor; the next pool (278) is an ordinary underwater sector, so swimming there was faster. The swim test (InUnderwaterSector) skipped such sectors too.
- 2026-10-21 Fix: Converter: underwater z-motion sectors get sOpen (their open range at OFF). Sim: SectorZRange gives their range now (sOpen moved with the moving plane); InUnderwaterSector and SectorAtPoint use it, so the pool is underwater and is the dude's sector.
