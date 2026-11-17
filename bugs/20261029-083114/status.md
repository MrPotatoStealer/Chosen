# 20261029-083114: sky has this dark areas now that is not supposed to be there

Status: fixed

## History

- 2026-10-29 08:31:14 reported in maps/e1m1.map
- 2026-10-29 fixed. Cause: the sky was shaded face by face from each face's vertex colour; the faces between two skies carry no shade of their own and drew darker
- 2026-10-29 Fix: one shade for the whole sky (the commonest sky ceiling shade, MapFaces.SkyShade), the background sky too
