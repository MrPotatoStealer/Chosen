# 20261020-101910: strange shading on this

Status: fixed
Regression: movers, sector-lighting
Regression check: play <id> to the end: the turned shelf's side is evenly shaded

## History

- 2026-10-20 10:19:10 reported in maps/e1m2.map
- 2026-10-20 fixed. Cause: Moving sectors are built at their OFF place but shade.json has Build's walls where Blood stores them (mostly ON): each triangle of a moved mover took the shade of whatever surface lay near its OFF place. One triangle of the turned bookshelf's (sector 12/13, rotOn 70) side found nothing (shade 16), the other an unrelated wall (57): a half-black face.
- 2026-10-20 Fix: Each mover triangle is looked up both where it was built and after its ON move (LevelLoader.MoverOnPoses: rotOn about the pivot, moveOn) and takes the surface it lies closer to (ShadeData.SurfaceShade's fit). A mover face whose surface belongs to another sector is left out of the mover's light effect (E1M1's secret shelf front). Test shots keep steady light effects (wave 0) on. New pickshade command prints the drawn triangle's shade.
