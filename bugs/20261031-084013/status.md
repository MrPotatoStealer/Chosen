# 20261031-084013: transparent water bug, missing the portal renderer for the other side

Status: fixed
Regression: water
Regression setup: send s1 on | 5
Regression check: the slid-open ice floe's water shows the room under it

## History

- 2026-10-31 08:40:13 reported in maps/e2m6.map
- 2026-10-31 fixed. Cause: The ice floe 1 is a sliding sector stored shut (zero width): its water link had no face at setup, so no portal was made and none appeared when it slid open.
- 2026-10-31 Fix: PortalRenderer.SetupNative keeps a portal without a face (not drawn) and NativeFaces makes its face and copies once its sector opens.
