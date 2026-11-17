namespace Chosen.Sim;

/// <summary>
/// The constants of NoOne's modern features (NBlood's nnexts.h, eventq.h): the object types, status lists, channels and
/// commands a modern map (.MAP version 7.1 or 7.2) uses beyond Blood's own (Data.K).
/// </summary>
public static class KM
{
    // debris sprites' physics (XSPRITE physAttr; kPhysMove 1, kPhysGravity 2, kPhysFalling 4 as a thing's flags)
    public const int kPhysDebrisFloat = 0x0008, kPhysDebrisFly = 0x0010, kPhysDebrisSwim = 0x0020, kPhysDebrisTouch = 0x0040,
        kPhysDebrisVector = 0x0400, kPhysDebrisExplode = 0x0800;

    // a modern type's sprite flags (hitag)
    public const int Flag1 = 0x0001, Flag2 = 0x0002, Flag3 = 0x0003, Flag4 = 0x0004, Flag8 = 0x0008, Flag16 = 0x0010,
        Flag32 = 0x0020, Flag64 = 0x0040, Flag128 = 0x0080, Flag256 = 0x0100;

    public const int kVectorGenDudePunch = 22;
    public const int kMaxRandomizeRetries = 16, kPercFull = 100, kMaxPlayers = 8;
    public const int kDudeFlagStealth = 0x0001, kDudeFlagIgnoreTouch = 0x0002, kDudeFlagCrouch = 0x0004;
    public const int kSlopeDist = 0x20, kEffectGenCallbackBase = 200;
    public const int kTriggerSpriteScreen = 0x0001, kTriggerSpriteAim = 0x0002;

    // status lists
    public const int kStatModernBase = 20, kStatModernDudeTargetChanger = 20, kStatModernCondition = 21,
        kStatModernEventRedirector = 22, kStatModernPlayerLinker = 23, kStatModernQavScene = 25, kStatModernPatrolRegion = 27,
        kStatModernMax = 40;

    // sprite types
    public const int kModernPatrolRegion = 16, kModernCustomDudeSpawn = 24, kModernRandomTX = 25, kModernSequentialTX = 26,
        kModernSeqSpawner = 27, kModernObjPropertiesChanger = 28, kModernObjPicnumChanger = 29, kModernObjSizeChanger = 31,
        kModernDudeTargetChanger = 33, kModernSectorFXChanger = 34, kModernObjDataChanger = 35, kModernSpriteDamager = 36,
        kModernObjDataAccumulator = 37, kModernEffectSpawner = 38, kModernWindGenerator = 39, kModernRandom = 40,
        kModernRandom2 = 80, kItemShroomGrow = 129, kItemShroomShrink = 130, kItemModernMapLevel = 150,
        kDudeModernCustom = 254, kModernThingTNTProx = 433, kModernThingThrowableRock = 434, kModernThingEnemyLifeLeech = 435,
        kModernPlayerControl = 500, kModernCondition = 501, kModernConditionFalse = 502, kModernSlopeChanger = 504,
        kModernLaserGen = 505, kModernVelocityChanger = 506, kGenModernMissileUniversal = 704, kGenModernSound = 708;

    public const int kModernSectorPathSprite = 611;

    public const int kRandomizeItem = 0, kRandomizeDude = 1, kRandomizeTX = 2;

    // channels
    public const int kChannelModernEndLevelCustom = 6, kChannelLevelStartNBLOOD = 17, kChannelLevelStartRAZE = 18,
        kChannelPlayer7 = 37, kChannelMapModernRev1 = 60, kChannelMapModernRev2 = 61;

    // commands
    public const int kCmdSectorMotionPause = 13, kCmdSectorMotionContinue = 14, kCmdDudeFlagsSet = 15, kCmdEventKillFull = 16,
        kCmdModernUse = 53, kCmdModernFeaturesEnable = 100;

    // callbacks
    public const int kCallbackMissileBurst = 22, kCallbackMissileSpriteBlock = 23, kCallbackFxPodGreenBloodSpray = 24;

    // sprite cstat bits Blood's own code doesn't name
    public const int CstatSlope = 48;     // floor aligned and sloped (CSTAT_SPRITE_ALIGNMENT_SLOPE)

    // the callbacks an effect generator may start on a sprite (data2 200..206)
    public static readonly int[] EffectGenCallbacks =
    {
        Data.K.kCallbackFXFlameLick, Data.K.kCallbackFXFlareSpark, Data.K.kCallbackFXFlareSparkLite, Data.K.kCallbackFXZombieSpurt,
        Data.K.kCallbackFXBloodSpurt, Data.K.kCallbackFXArcSpark, Data.K.kCallbackFXTeslaAlt,
    };

    // gExplodeExtra: an explosion kind's sequence, sound, and whether it is a ground one
    public static readonly (int seq, int snd, bool ground)[] ExplodeExtra =
    {
        (3, 303, true), (4, 304, false), (4, 305, false), (9, 307, true), (5, 307, true), (4, 303, false), (4, 303, false),
        (4, 303, false),
    };

    // gVectorInfoExtra: a vector's two fire sounds (the custom dudes')
    public static readonly int[] VectorFireSound =
    {
        1207, 1207, 1001, 1001, 4001, 4002, 431, 431, 1002, 1002, 359, 359, 521, 521, 513, 513, 499, 499, 9012, 9014, 1101, 1101,
        1207, 1207, 499, 495, 495, 496, 9013, 499, 1307, 1308, 499, 499, 499, 499, 499, 499, 499, 499, 351, 351, 0, 0, 357, 499,
    };

    // gMissileInfoExtra: a missile's two fire sounds, the damage types it may use, and whether it triggers Impact objects
    public static readonly (int snd0, int snd1, bool[] dmg, bool impact)[] MissileExtra =
    {
        (1207, 1207, new[] { false, false, false, false, false, true, false }, true),
        (420, 420, new[] { false, true, true, false, false, false, false }, true),
        (471, 471, new[] { false, false, false, false, false, false, true }, false),
        (421, 421, new[] { false, true, false, true, false, false, false }, false),
        (1309, 351, new[] { false, true, false, false, false, false, false }, true),
        (480, 480, new[] { false, true, false, true, false, false, false }, false),
        (470, 470, new[] { false, false, false, false, false, false, true }, true),
        (489, 490, new[] { false, false, false, false, false, true, false }, true),
        (462, 351, new[] { false, true, false, false, false, false, false }, true),
        (1203, 172, new[] { false, false, true, false, false, false, false }, true),
        (0, 0, new[] { false, false, true, false, false, false, false }, true),
        (1457, 249, new[] { false, false, false, false, false, true, false }, true),
        (480, 489, new[] { false, true, false, true, false, false, false }, false),
        (480, 489, new[] { false, false, false, true, false, false, false }, false),
        (480, 489, new[] { false, false, false, true, false, false, false }, false),
        (491, 491, new[] { true, true, true, true, true, true, true }, true),
        (520, 520, new[] { false, false, false, false, false, true, false }, true),
        (520, 520, new[] { false, false, false, false, false, true, false }, true),
    };

    // gThingInfoExtra: may a custom dude throw the thing (400..435)
    public static readonly bool[] ThingAllowThrow =
    {
        true, true, true, false, false, false, false, false, false, false, false, false, false, false, false, true, false, false,
        true, true, true, true, false, false, false, false, false, true, true, true, true, true, true, true, true, true,
    };

    // gDudeInfoExtra (200..255): flying, melee, and the sequence offsets patrolling uses: idle and move on the ground,
    // in water, crouching (-1: none)
    public static readonly (bool flying, bool melee, int idlg, int mveg, int idlw, int mvew, int idlc, int mvec)[] DudeExtra =
    {
        (false, false, -1, -1, -1, -1, -1, -1),     // 200
        (false, false, 0, 9, 13, 13, 17, 14),       // 201
        (false, false, 0, 9, 13, 13, 17, 14),       // 202
        (false, true, 0, 8, 0, 8, -1, -1),          // 203
        (false, false, 0, 8, 0, 8, -1, -1),         // 204
        (false, true, 12, -1, 12, -1, -1, -1),      // 205
        (true, true, 0, 0, 0, 0, -1, -1),           // 206
        (true, false, 0, 0, 0, 0, -1, -1),          // 207
        (false, false, 0, 0, 0, 0, -1, -1),         // 208
        (false, false, 0, 0, 0, 0, -1, -1),         // 209
        (true, true, 0, 0, 0, 0, -1, -1),           // 210
        (false, true, 0, 8, 0, 8, -1, -1),          // 211
        (false, true, 0, 6, 0, 6, -1, -1),          // 212
        (false, true, 0, 7, 0, 7, -1, -1),          // 213
        (false, true, 0, 7, 0, 7, -1, -1),          // 214
        (false, true, 0, 7, 0, 7, -1, -1),          // 215
        (false, true, 0, 7, 0, 7, -1, -1),          // 216
        (false, true, 0, 9, 10, 10, -1, -1),        // 217
        (false, true, 0, 0, 0, 0, -1, -1),          // 218
        (true, false, 7, 7, 7, 7, -1, -1),          // 219
        (false, true, 0, 7, 0, 7, -1, -1),          // 220
        (false, false, 0, -1, 0, -1, -1, -1),       // 221
        (false, true, 0, -1, 0, -1, -1, -1),        // 222
        (false, false, 0, -1, 0, -1, -1, -1),       // 223
        (false, true, 0, -1, 0, -1, -1, -1),        // 224
        (false, false, 0, -1, 0, -1, -1, -1),       // 225
        (false, false, -1, -1, -1, -1, -1, -1),     // 226
        (false, false, 0, 7, 0, 7, -1, -1),         // 227
        (false, false, 0, 7, 0, 7, -1, -1),         // 228
        (false, false, 0, 8, 0, 8, -1, -1),         // 229
        (false, false, 0, 9, 13, 13, 17, 14),       // 230
        (false, false, -1, -1, -1, -1, -1, -1),     // 231
        (false, false, -1, -1, -1, -1, -1, -1),     // 232
        (false, false, -1, -1, -1, -1, -1, -1),     // 233
        (false, false, -1, -1, -1, -1, -1, -1),     // 234
        (false, false, -1, -1, -1, -1, -1, -1),     // 235
        (false, false, -1, -1, -1, -1, -1, -1),     // 236
        (false, false, -1, -1, -1, -1, -1, -1),     // 237
        (false, false, -1, -1, -1, -1, -1, -1),     // 238
        (false, false, 3, 3, 3, 3, -1, -1),         // 239
        (false, false, 3, 3, 3, 3, -1, -1),         // 240
        (false, false, 3, 3, 3, 3, -1, -1),         // 241
        (false, false, 3, 3, 3, 3, -1, -1),         // 242
        (false, false, -1, -1, -1, -1, -1, -1),     // 243
        (false, true, 10, -1, 10, -1, -1, -1),      // 244
        (false, true, 0, 6, 0, 6, -1, -1),          // 245
        (false, false, 0, 9, 13, 13, 17, 14),       // 246
        (false, false, 0, 9, 13, 13, 14, 14),       // 247
        (false, false, 0, 9, 13, 13, 14, 14),       // 248
        (false, false, 0, 9, 13, 13, 17, 14),       // 249
        (false, true, 0, 6, 8, 8, 10, 9),           // 250
        (false, true, 0, 8, 9, 9, 11, 10),          // 251
        (false, false, -1, -1, -1, -1, -1, -1),     // 252
        (false, false, -1, -1, -1, -1, -1, -1),     // 253
        (false, false, 0, 4, 0, 4, 0, 4),           // 254 (a custom dude: its own states')
        (false, false, -1, -1, -1, -1, -1, -1),     // 255
    };
}
