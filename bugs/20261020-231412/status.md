# 20261020-231412: killed the boss but level is not finished

Status: fixed
Regression: collision
Regression check: kill the gargoyles in the sky-lit courtyard: none flies out over the walls; after all of them die the exit opens

## History

- 2026-10-20 23:14:12 reported in maps/e1m7.map
- 2026-10-21 fixed. Cause: E1M7's exit opens when a combo switch counts every gargoyle's death (5 at skill 3); 3 flesh gargoyles had flown out of the level and could not be killed. A gargoyle rising into the sky brush over sectors 37/46 started GetZRange inside solid, whose search upward for room put its floor on the brush's top: it was lifted onto the roof of the map
- 2026-10-21 Fix: Physics.GetZRange: a sprite that rose into a ceiling this frame (zvel < 0, clear where it came from) traces from the highest clear point below, so MoveDude's ceiling clamp pushes it back under, as Build does; no gargoyle leaves the map in the replay
