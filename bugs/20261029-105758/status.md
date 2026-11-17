# 20261029-105758: lots of flickering geo when riding on boat (immortal or it will kill me)

Status: fixed

## History

- 2026-10-29 10:57:58 reported in maps/dwe3m1.map
- 2026-10-29 note: no flicker in sampled ride frames; a thin white strip beside the far dock shows only from a distance during the ride: asked the user what flickers
- 2026-10-31 fixed. Cause: Not reproduced on the native world: 100 consecutive drawn frames of the ride (replay at frames 1250 and 1450, shotburst) show no pixels that come and go beyond the first frames' fading messages. The report's renderer has been replaced since.
- 2026-10-31 Fix: None; reopen with what flickers if it comes back.
