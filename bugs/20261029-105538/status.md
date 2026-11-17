# 20261029-105538: when looking this way the trees cut off, but not if I look directly at them

Status: fixed

## History

- 2026-10-29 10:55:38 reported in maps/dwe3m1.map
- 2026-10-29 fixed. Cause: sprites of sectors outside the visibility walk's view window lost their sky mark: trees beside the view cut at the sky
- 2026-10-29 Fix: a sector within three open walls of the camera's counts as seen for the mark (SectorVisibility.DirectlySeen), partners over or under through a link excluded
