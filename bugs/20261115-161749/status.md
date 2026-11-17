# 20261115-161749: add a setting to increase dynamic light range

Status: fixed

## History

- 2026-11-15 16:17:49 reported in maps/sstation.map
- 2026-11-15 fixed. Cause: A request: the dynamic lights' reach was fixed (each light's radius as the map or the game gives it).
- 2026-11-15 Fix: r_lightrange (saved; Options > Enhanced > Lighting, LIGHT RANGE, 0.5 to 3): every dynamic light's radius times it (DynamicLights.Add), the links a light reaches through worked out with it; the regression shots pin it to 1.
