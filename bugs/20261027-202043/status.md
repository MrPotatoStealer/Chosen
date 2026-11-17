# 20261027-202043: thunder sound is cery lound and sounds wrong\

Status: fixed

## History

- 2026-10-27 20:20:43 reported in maps/e4m1.map
- 2026-10-27 fixed. Cause: E4M1's thunder is 84 sound sprites on channel 100 playing thunder2.wav at once; our 3D volume was relVol/80 (relVol 80 = full scale), 2.55x NBlood's 0..255 scale, so the sum clipped about 10x
- 2026-10-27 Fix: SoundSystem uses Calc3DValues' relVol*80/dist on 0..255 (sndStartSample relVol*80 for local sounds), volume 1 = FXVolume 255 by default, and a hard limiter on the master bus
