# 20261020-231435: think boss don't have correct hp, died very fast

Status: fixed

## History

- 2026-10-20 23:14:35 reported in maps/e1m7.map
- 2026-10-21 note: Not a bug as far as NBlood goes: the stone gargoyle starts with 3200 (200 << 4, NBlood dudeInfo), the Tesla's factor at skill 3 is 52 (64 * 208 >> 8); it took 49 Tesla hits (~63 each) over ~11 s in the replay. The level-end part was 20261020-231412 (escaped flesh gargoyles).
- 2026-10-21 note: blood-wiki Cheogh HP (2560/3414/5120/6302/8192) = 5120*256/DudeDifficulty[skill]; 5120 is the stone gargoyle's seqStartID in dudeInfo, not its health (startHealth 200 -> 3200). The wiki's own Stone Gargoyle page gives different, non-proportional numbers. NBlood: 3200 health, damage scaled by curDamage (Tesla 64*DudeDifficulty/256).
- 2026-10-21 fixed. Cause: Not a bug: Cheogh is a stone gargoyle, 200 health (3200), damage scaled by skill as in NBlood; the user verified it against the game.
- 2026-10-21 Fix: None needed.
