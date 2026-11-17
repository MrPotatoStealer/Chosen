# 20261022-103239: bookcause works creat just this sprite is following it along with things on the floor, will compare to NBlood but the portrait is strange

Status: fixed

## History

- 2026-10-22 10:32:39 reported in maps/e2m5.map
- 2026-10-22 fixed. Cause: Blood's Drag carries only sprites in the turning sector standing on its floor, not wall or floor sprites; Godot took everything inside the bookcase's outline standing on any floor, including room 472's portrait (a wall sprite at z 100) and things on 472's floor lower down. The carried player's view also snapped to the turn once per game frame
- 2026-10-22 Fix: Drag riders must stand on the sector's floor (sOpen, now also given to Drag sectors) and not be wall/floor sprites; a mover's turn of the player's view is spread over the next game frame (Player.MoverTurn, PlayerControls.Turn)
