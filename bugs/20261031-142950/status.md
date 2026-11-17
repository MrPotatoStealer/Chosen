# 20261031-142950: changing weather settings is still somewhat bugged and is either not responding or removing all weather

Status: fixed

## History

- 2026-10-31 14:29:50 reported in maps/e3m1.map
- 2026-10-31 fixed. Cause: A weather layer made after the grid (changing r_weather while playing) was never told where the grid lies (grid_corner, grid_size only sent when a grid was finished), so its particles were all outside it and died, until the camera moved far enough for a new grid: the weather gone. Rain and weather amounts themselves worked.
- 2026-10-31 Fix: Weather.AddLayer sends the grid to a new layer. Tested in one running session with `later`: off, rain, rain more and less, weather less and more, off, level, snow, rain: each responds.
