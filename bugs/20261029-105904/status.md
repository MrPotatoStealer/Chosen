# 20261029-105904: AO looks strange on this level, something we can do to take the fog into consideration when applying the AO?

Status: fixed

## History

- 2026-10-29 10:59:04 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: AO darkened surfaces the white fog had covered
- 2026-10-29 Fix: fogged surfaces write a lower roughness (fog_roughness); AO takes that share off
