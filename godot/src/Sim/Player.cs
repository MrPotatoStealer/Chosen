using Godot;

namespace Chosen.Sim;

/// <summary>Blood's hit codes (gameutil.h): 0x4000 sector (floor/ceiling), 0x8000 wall, 0xc000 sprite.</summary>
public static class Hit
{
    public const int None = 0, Sector = 16384, Wall = 32768, Sprite = 49152;
}

/// <summary>
/// A player: Blood's PLAYER on the player's dude sprite (Blood3D's QuakeC kept both on the client entity). Field
/// names follow Blood3D's (and the bug recordings'), so a recording's player state maps onto it directly. Position is
/// Quake units; velocities are Blood units (65536 = 1 Quake unit per game frame, z positive down); zView/zWeapon are
/// absolute Build z.
/// </summary>
public sealed class Player : Sprite
{
    public const int PostureStand = 0, PostureSwim = 1, PostureCrouch = 2;
    public const int MediumNormal = 0, MediumWater = 1, MediumGoo = 2;
    public const int DudePlayer1 = 231;
    public const int ModeHuman = 0, ModeBeast = 1, ModeHumanShrink = 2, ModeHumanGrown = 3;
    public const int PackMedKit = 0, PackDivingSuit = 1, PackCrystalBall = 2, PackBeastVision = 3, PackJumpBoots = 4, PackMax = 5;

    public Player()
    {
        ClassName = "player";
        Type = InitType = DudePlayer1;
        StatNum = Data.K.kStatDude;
        ClipDist = 48;
        Cstat = Sim.Cstat.YCenter | Sim.Cstat.Block | Sim.Cstat.Hitscan;
        HitTag = 15;                     // physics flags: 1 move, 2 gravity, 4 falling
        XHealth = 100 * 16;
    }

    public int PlayerIndex, TeamId;
    public string NetName = "Player";
    public bool Ready;
    public int AngOld;
    public bool FixAngle;                    // the server turned the player (playerSetViewYaw): the view takes Ang
    public float MoverTurn;                  // a mover carrying the player turned it (Quake degrees): the view turns as much
    public bool NoClip, GodMode, NoTarget;

    // PLAYER
    public int LifeMode;
    public int Posture;
    public double ZView, ZViewVel, ZWeapon, ZWeaponVel;
    public double BobPhase, BobAmp, SwayAmp, BobHeight, BobWidth, SwayHeight, SwayWidth;
    public double Look, Horiz, SlopeHoriz, Slope;
    public double Spin;
    public bool CantJump, FallScream, IsRunning, IsUnderwater;
    public double BubbleTime, RestTime, DeathTime, UnderwaterTime = 1200;
    public int HasFlag;
    public double KickPower;
    public int LaughCount;
    public int PackItemTime, PackItemId = -1;
    public Sprite FraggerId;
    public int FragCount;
    public double FlickerEffect, QuakeEffect, TiltEffect, Visibility, PainEffect, BlindEffect, ChokeEffect;
    public double HandTime, PickupEffect, FlashEffect;
    public int Hand;
    public int ChokeCounter;           // gChokeCounter: tics towards the next playerHandChoke while a hand holds on

    // weapons (weapon.qc)
    public int CurWeapon, NextWeapon, LastWeapon, WeaponState, WeaponAmmo, WeaponQav = -1;
    public double WeaponTimer, WeaponQavStart;
    public System.Action<int, Player> QavCallback;
    public bool QavLoop;
    public double FuseTime, ThrowTime, ThrowPower;
    public double AimDx, AimDy, AimDz, RelAimDx = 16384, RelAimDy, RelAimDz;
    public Sprite AimTarget;
    public int AimTargetsCount;
    public readonly Sprite[] AimTargets = new Sprite[16];
    public Sprite VoodooTarget;
    public int VoodooTargets;
    public double VoodooVar1, VoodooVar2;

    // inventory
    public readonly double[] PwUpTime = new double[51];
    public readonly int[] HasWeapon = new int[14];
    public readonly int[] WeaponMode = new int[14];
    public readonly double[] Ammo = new double[12];
    public readonly int[] HasKey = new int[8];
    public readonly int[] DamageControl = new int[7];
    public readonly int[] PackActive = new int[5];
    public readonly double[] PackAmount = new double[5];
    public readonly double[] Armor = new double[3];

    public PlayerInput Cmd;
    public double PrevZView, PrevZWeapon, PrevBZ, PrevBobHeight, PrevBobWidth, PrevSwayHeight, PrevSwayWidth;   // at the previous frame (the view interpolates)                  // the input for the next game frame (the client sets it)

    // the frame's input (playerLatchInput)
    public double InputForward, InputStrafe;
    public bool InputShoot, InputShoot2, InputJump, InputCrouch, InputAction, InputSpin180, LastButton5;
    public bool InputLookCenter, InputNextWeapon, InputPrevWeapon, InputLastWeapon, InputHolster;
    public bool InputUseItem, InputNextItem, InputPrevItem;
    public bool UseBeastVision, UseCrystalBall, UseJumpBoots, UseMedKit;
    public int InputNewWeapon;
    public int InputImpulse;

    // a modern map's player control: movement speed and jump height in percents of the postures' own, and the QAV
    // scene it plays (-1: none)
    public int MoveSpeedPerc = 100, JumpHeightPerc = 100, SceneQav = -1;

    /// <summary>gPosture[lifeMode][posture][field] (posture table: 4 life modes x 3 postures x 15), the accelerations
    /// and the standing jump scaled as a player control set them.</summary>
    public double PostureInfo(int field)
    {
        double v = Data.GenTables.tbl_posture[LifeMode * 45 + Posture * 15 + field];
        if (field <= PO_backAccel)
            return MoveSpeedPerc == 100 ? v : Blood.Int(v * MoveSpeedPerc / 100);
        if (field >= PO_normalJumpZ && Posture == PostureStand && JumpHeightPerc != 100)
            return Blood.Int(v * JumpHeightPerc / 100);
        return v;
    }

    public const int PO_frontAccel = 0, PO_sideAccel = 1, PO_backAccel = 2, PO_pace0 = 3, PO_pace1 = 4, PO_bobV = 5,
        PO_bobH = 6, PO_swayV = 7, PO_swayH = 8, PO_eyeAboveZ = 9, PO_weaponAboveZ = 10, PO_xOffset = 11, PO_zOffset = 12,
        PO_normalJumpZ = 13, PO_pwupJumpZ = 14;
}
