# 20261030-191704: changing rain amount or weather amount does nothing while playing, maybe restart work?

Status: fixed

## History

- 2026-10-30 19:17:04 reported in maps/e1m1.map
- 2026-10-30 fixed. Cause: Weather changed the particle layers' AmountRatio, which Godot doesn't apply to particles respawning under the custom process shader: every particle kept falling
- 2026-10-30 Fix: The layer's particle count is set from the amount instead (Weather.Update); a new count restarts the layer, filled at once by its preprocess
