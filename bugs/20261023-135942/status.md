# 20261023-135942: difficult going into the water

Status: fixed

## History

- 2026-10-23 13:59:42 reported in maps/e2m6.map
- 2026-10-23 fixed. Cause: The water link on the strip stayed at its closed height (80): link planes follow a mover matched by its brush, and a live sector's mover is only a placeholder; with the water at 72 the player never crossed into it.
- 2026-10-23 Fix: Links find a live sector's plane mover by its plane and area as built.
