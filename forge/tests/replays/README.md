# Forge regression replays

Each folder is a recorded Forge session (`src/Recording/SessionRecorder.cs`) kept as a test:

- `events.log`: what was done, event by event (mouse and keys in map units, commands, dialog results, settings, the
  view), with the map's fingerprint after every edit;
- `map-NN-original.*`: a map the session opened, as it was before anything was done to it; `map-NN-original.*.ref`
  instead names a level of the data folder (`game/blood3d/...`: Blood's and the add-ons' .MAP files, not committed here; a
  .MAPX there, the mapper's own such as `test.mapx`, is always copied: it changes when saved);
- `doc-NN.mapx`: in older sessions, the map after a dialog (a replay restores it; sessions now record the fields
  changed, and a replay fills the same dialog);
- `expected.txt`: the fingerprints a correct replay gives (written by `--accept`); without it the recorded ones;
- `note.txt`: what the session does or which problem it reproduces.

`forge\run.bat --regress` replays them all (after changing Forge, it must pass with `--roundtrip`, `--selftest` and
`--uitest`). A session becomes a test with `forge\run.bat --addtest=<session folder or last> --name=<name>
[--note="..."]` (the session is replayed once and its checks accepted). After a fix changes what a test's session
should produce, check it does the right thing, then `forge\run.bat --replay=forge/tests/replays/<name> --accept`.
