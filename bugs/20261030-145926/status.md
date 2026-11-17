# 20261030-145926: sky is affected by fogs light

Status: fixed

## History

- 2026-10-30 14:59:26 reported in maps/e1m1.map
- 2026-10-30 fixed. Cause: A converted level's sky is drawn on its sky faces (level surfaces): the enhanced fog's depth fade and its lights' glow lay over them; the environment's sky affect only reaches the background
- 2026-10-30 Fix: The sky faces' shader has fog_disabled
