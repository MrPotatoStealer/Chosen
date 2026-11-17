# 20261020-231956: flashing wall

Status: fixed

## History

- 2026-10-20 23:19:56 reported in maps/e1m4.map
- 2026-10-21 note: Sector 204 (Jo-Jo booth) has Blood's light effect: wave 5 (sine), amplitude 64, frequency 200, shade always: pulses ~12 times a second in NBlood too; the port's wave matches sectorfx.cpp. Asked the user whether it looks different from NBlood.
- 2026-10-21 fixed. Cause: Not a bug: sector 204's sine light (wave 5, amplitude 64, frequency 200) pulses ~12 times a second, as in NBlood; the user confirmed it is correct.
- 2026-10-21 Fix: None needed.
