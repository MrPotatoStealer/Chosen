# 20261026-131047: hands are unable to grab the player?

Status: fixed

## History

- 2026-10-26 13:10:47 reported in maps/e3m1.map
- 2026-10-26 fixed. Cause: The hand's grab worked (player.hand set, the hand removed) but nothing ran gChoke: no hand drawn over the view, no choking (handTime never grew, so use shook it off at once, no darkening)
- 2026-10-26 Fix: World.Tick runs PlayerHandChoke each second while a hand holds on (gChokeCounter); Hud.DrawChoke draws QAV 518 over the view and status bar
